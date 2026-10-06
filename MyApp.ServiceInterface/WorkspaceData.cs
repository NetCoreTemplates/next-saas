using System.Data;
using System.Reflection;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.DataAnnotations;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

/// <summary>
/// Everything an organization owns, so its export and deletion cover every table without listing them.
/// A new table implementing <see cref="IHasWorkspaceId"/> is exported and deleted with its organization unless
/// it's added to <see cref="NotExported"/> or <see cref="KeptAfterDeletion"/>, with the reason why.
/// </summary>
public static class WorkspaceData
{
    /// <summary>
    /// The tables owned by an organization, ordered so rows are deleted before the rows they reference
    /// </summary>
    public static readonly IReadOnlyList<Type> Tables = InDeleteOrder(typeof(IHasWorkspaceId).Assembly.GetTypes()
        .Where(x => x is { IsClass: true, IsAbstract: false } && typeof(IHasWorkspaceId).IsAssignableFrom(x))
        .OrderBy(x => x.Name));

    /// <summary>
    /// Tables that aren't part of a customer's export, and why
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> NotExported = new Dictionary<Type, string> {
        [typeof(UsageReservation)] = "Short-lived holds on quota while an operation is in progress",
        [typeof(UsageDailyRollup)] = "Derived from the usage events that are exported",
        [typeof(SupportNote)] = "Operator notes about the customer",
        [typeof(DataExportArtifact)] = "The customer's previous exports",
        [typeof(NotificationDelivery)] = "Copies of messages that were sent to members",
    };

    /// <summary>
    /// Tables whose rows remain after their organization is deleted, and why
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> KeptAfterDeletion = new Dictionary<Type, string> {
        [typeof(WorkspaceLifecycleRequest)] = "The record of the deletion that was requested and completed",
        [typeof(SaasAuditEvent)] = "The audit trail, which is kept until its retention period ends",
    };

    // Columns that are part of how the row is stored or secured, not the customer's data
    private static readonly Dictionary<Type, Action<object>> Redactions = new() {
        [typeof(WorkspaceMember)] = row => ((WorkspaceMember)row).InvitationTokenHash = null,
        [typeof(StoredFile)] = row => ((StoredFile)row).ObjectKey = "",
    };

    public static IEnumerable<Type> ExportedTables => Tables.Where(x => !NotExported.ContainsKey(x)).OrderBy(x => x.Name);
    public static IEnumerable<Type> DeletedTables => Tables.Where(x => !KeptAfterDeletion.ContainsKey(x));

    /// <summary>
    /// The organization's rows of every exported table as JSON, by table name.
    /// The connection must be confined to the organization.
    /// </summary>
    public static Dictionary<string, string> ExportJson(IDbConnection db)
    {
        RequireConfined(db);
        return ExportedTables.ToDictionary(x => x.Name, x => SelectJson(db, x));
    }

    /// <summary>
    /// Delete the organization's rows of every table it owns, returning how many rows of each were deleted.
    /// The connection must be confined to the organization.
    /// </summary>
    public static Dictionary<string, int> DeleteAll(IDbConnection db)
    {
        RequireConfined(db);
        return DeletedTables.ToDictionary(x => x.Name, x => db.DeleteAll(x));
    }

    private static string RequireConfined(IDbConnection db) => db.GetWorkspaceId()
        ?? throw new InvalidOperationException("An organization's data is exported and deleted on a connection confined to it, see SaasDb.ForWorkspace.");

    private static string SelectJson(IDbConnection db, Type table)
    {
        // A List of the table's Type, so it's serialized with the table's columns
        var rows = db.CreateTypedApi(table).Select();
        if (Redactions.TryGetValue(table, out var redact))
        {
            foreach (var row in rows)
                redact(row);
        }
        return rows.ToJson();
    }

    // Rows that reference other rows come first, e.g. UsageAggregate before the UsagePeriod it references
    private static List<Type> InDeleteOrder(IEnumerable<Type> tables)
    {
        var remaining = tables.ToList();
        var ordered = new List<Type>();
        while (remaining.Count > 0)
        {
            var referenced = new HashSet<Type>(remaining.SelectMany(References).Where(remaining.Contains));
            var next = remaining.Where(x => !referenced.Contains(x)).ToList();
            if (next.Count == 0)
                throw new InvalidOperationException($"Tables reference each other in a cycle: {string.Join(", ", remaining.Select(x => x.Name))}");
            ordered.AddRange(next);
            remaining.RemoveAll(next.Contains);
        }
        return ordered;
    }

    private static IEnumerable<Type> References(Type table) => table.GetProperties()
        .Select(x => x.GetCustomAttribute<ReferencesAttribute>()?.Type)
        .Where(x => x != null && x != table)!;
}

/// <summary>
/// The stored objects of one organization. Keys are created here and checked on every use, so code with another
/// organization's key, or a key from a request, can't read, replace or delete an object outside the organization.
/// </summary>
public sealed class WorkspaceFiles
{
    private readonly IFileStore store;
    private readonly string prefix;

    internal WorkspaceFiles(IFileStore store, string workspaceId)
    {
        if (workspaceId.IsNullOrEmpty() || workspaceId.Contains("..") ||
            !workspaceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new ArgumentException("The organization id can't be used in a storage key.", nameof(workspaceId));
        this.store = store;
        prefix = $"workspaces/{workspaceId}/";
    }

    /// <summary>A new key for a file. Keys are opaque: the customer's file name is only kept as metadata.</summary>
    public string NewFileKey(string? id = null) => $"{prefix}files/{id ?? Guid.NewGuid().ToString("N")}";

    public string ExportKey(string lifecycleRequestId) => $"{prefix}exports/{lifecycleRequestId}.zip";

    public Task<FileStoreWriteResult> WriteAsync(string objectKey, Stream source, long maximumBytes, CancellationToken token = default) =>
        store.WriteAsync(Require(objectKey), source, maximumBytes, token);

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken token = default) =>
        store.OpenReadAsync(Require(objectKey), token);

    public Task DeleteAsync(string objectKey, CancellationToken token = default) =>
        store.DeleteAsync(Require(objectKey), token);

    private string Require(string objectKey)
    {
        var normalized = objectKey.Replace('\\', '/');
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || normalized.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidOperationException("The storage key doesn't belong to this organization.");
        return normalized;
    }
}

public static class WorkspaceJobs
{
    /// <summary>
    /// Queue work for one organization. The organization is the job's TenantId, so an organization's jobs can
    /// be found in the jobs dashboard, and its payload says which organization to confine its connection to.
    /// </summary>
    public static BackgroundJobRef EnqueueForWorkspace<TCommand>(this IBackgroundJobs jobs, string workspaceId, object request)
        where TCommand : IAsyncCommand =>
        jobs.EnqueueCommand<TCommand>(request, new BackgroundJobOptions { TenantId = workspaceId });
}

public static class WorkspaceFilesExtensions
{
    /// <summary>
    /// The file store confined to one organization. Use it instead of the store itself.
    /// </summary>
    public static WorkspaceFiles ForWorkspace(this IFileStore files, string workspaceId) => new(files, workspaceId);
}
