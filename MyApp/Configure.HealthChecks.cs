using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MyApp.ServiceInterface;
using ServiceStack.Data;
using ServiceStack.OrmLite;

[assembly: HostingStartup(typeof(MyApp.HealthChecks))]

namespace MyApp;

public class HealthChecks : IHostingStartup
{
    private sealed class LivenessCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }

    private sealed class DatabaseReadinessCheck(IDbConnectionFactory dbFactory) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default)
        {
            try
            {
                using var db = dbFactory.Open();
                db.SqlScalar<long>("SELECT 1");
                if (!db.TableExists<ServiceModel.Workspace>() || !db.TableExists<ServiceModel.SaasPlan>())
                    return Task.FromResult(HealthCheckResult.Unhealthy("The application database schema is incomplete."));
                return Task.FromResult(HealthCheckResult.Healthy());
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("The application database is unavailable.", ex));
            }
        }
    }

    private sealed class StorageReadinessCheck(FileStorageConfig config) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default)
        {
            string? probe = null;
            try
            {
                var root = Path.GetFullPath(config.RootPath);
                Directory.CreateDirectory(root);
                probe = Path.Combine(root, $".readiness-{Guid.NewGuid():N}");
                await File.WriteAllTextAsync(probe, "ready", token);
                File.Delete(probe);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                if (probe != null) try { File.Delete(probe); } catch { }
                return HealthCheckResult.Unhealthy("The configured file store is unavailable or not writable.", ex);
            }
        }
    }

    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddHealthChecks()
                .AddCheck<LivenessCheck>("application", tags: ["live"])
                .AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"])
                .AddCheck<StorageReadinessCheck>("storage", tags: ["ready"]);
            services.AddTransient<IStartupFilter, StartupFilter>();
        });
    }

    public class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // Keep anonymous health responses intentionally terse; detailed dependency
            // diagnostics belong in authenticated administration and structured logs.
            app.UseHealthChecks("/up", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
            app.UseHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
            next(app);
        };
    }
}
