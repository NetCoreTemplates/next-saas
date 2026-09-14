using System.Security.Cryptography;
using MyApp.ServiceInterface;

[assembly: HostingStartup(typeof(MyApp.ConfigureFileStorage))]

namespace MyApp;

public class ConfigureFileStorage : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((context, services) =>
    {
        var defaults = new FileStorageConfig();
        var config = new FileStorageConfig { AllowedExtensions = [] };
        context.Configuration.GetSection("FileStorage").Bind(config);
        if (config.AllowedExtensions.Count == 0) config.AllowedExtensions = defaults.AllowedExtensions;
        if (config.MaxFileBytes <= 0) throw new InvalidOperationException("FileStorage:MaxFileBytes must be greater than zero.");
        services.AddSingleton(config);
        services.AddSingleton<IFileStore>(_ => new LocalFileStore(config, context.HostingEnvironment.ContentRootPath));
    });
}

public class LocalFileStore(FileStorageConfig config, string contentRoot) : IFileStore
{
    private readonly string root = Path.GetFullPath(Path.IsPathRooted(config.RootPath)
        ? config.RootPath
        : Path.Combine(contentRoot, config.RootPath));

    public async Task<FileStoreWriteResult> WriteAsync(string objectKey, Stream source, long maximumBytes, CancellationToken token = default)
    {
        var path = Resolve(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".upload-" + Guid.NewGuid().ToString("N");
        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token);
                    if (read == 0) break;
                    total += read;
                    if (total > maximumBytes)
                        throw new HttpError(413, "FileTooLarge", $"The uploaded file exceeds the {maximumBytes} byte limit.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                await output.FlushAsync(token);
            }
            File.Move(temporaryPath, path, true);
            return new FileStoreWriteResult(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken token = default)
    {
        var path = Resolve(objectKey);
        if (!File.Exists(path)) throw new HttpError(404, "StoredObjectNotFound", "The stored file could not be found.");
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string objectKey, CancellationToken token = default)
    {
        var path = Resolve(objectKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool Exists(string objectKey) => File.Exists(Resolve(objectKey));

    private string Resolve(string objectKey)
    {
        if (objectKey.IsNullOrEmpty()) throw new ArgumentException("Object key is required.", nameof(objectKey));
        var normalized = objectKey.Replace('\\', '/').TrimStart('/');
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new HttpError(400, "InvalidObjectKey", "The storage object key is invalid.");
        return path;
    }
}
