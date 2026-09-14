using Microsoft.EntityFrameworkCore;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using MyApp.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

[assembly: HostingStartup(typeof(MyApp.ConfigureDb))]

namespace MyApp;

public class ConfigureDb : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context, services) => {
            var provider = context.Configuration.GetValue("Database:Provider", "Sqlite").Trim();
            var configuredConnection = context.Configuration.GetConnectionString("DefaultConnection")
                                       ?? "Data Source=App_Data/app.db;Cache=Shared";
            if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                if (configuredConnection.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Database.Provider is PostgreSql but DefaultConnection is a SQLite connection string.");
                services.AddOrmLite(options => options.UsePostgres(configuredConnection));
                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(configuredConnection));
            }
            else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                // EF Core accepts a bare DataSource value, while OrmLite can interpret
                // one without additional options as a literal file name. Canonicalize
                // once so both data stacks always open the same SQLite database.
                var sqlite = new SqliteConnectionStringBuilder(configuredConnection);
                if (sqlite.Cache == SqliteCacheMode.Default)
                    sqlite.Cache = SqliteCacheMode.Shared;
                var connectionString = sqlite.ToString();
                services.AddOrmLite(options => options.UseSqlite(connectionString));
                services.AddDbContext<ApplicationDbContext>(options => {
                    options.UseSqlite(connectionString, b => b.MigrationsAssembly(nameof(MyApp)));
                    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                });
            }
            else
            {
                throw new InvalidOperationException($"Unsupported Database.Provider '{provider}'. Use Sqlite or PostgreSql.");
            }
            
            // Enable built-in Database Admin UI at /admin-ui/database
            services.AddPlugin(new AdminDatabaseFeature());
        });
}
