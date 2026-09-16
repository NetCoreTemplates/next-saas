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
    /// <summary>
    /// Database:Provider values this application accepts, normalized to one canonical name so
    /// configuration, the Kamal destination, and the deployment scripts all agree.
    /// </summary>
    public static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "sqlite" => "sqlite",
        "postgres" or "postgresql" => "postgres",
        "sqlserver" or "mssql" => "sqlserver",
        "mysql" or "mariadb" => "mysql",
        _ => throw new InvalidOperationException(
            $"Unsupported Database.Provider '{provider}'. Use Sqlite, PostgreSql, SqlServer, or MySql."),
    };

    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context, services) => {
            var provider = NormalizeProvider(context.Configuration.GetValue("Database:Provider", "Sqlite")!);
            var configuredConnection = context.Configuration.GetConnectionString("DefaultConnection")
                                       ?? "Data Source=App_Data/app.db;Cache=Shared";

            // A file connection string against a server provider silently produces a database
            // nobody intended, so fail loudly instead.
            if (provider != "sqlite" && configuredConnection.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Database.Provider is {provider} but DefaultConnection is a SQLite connection string.");

            switch (provider)
            {
                case "postgres":
                    services.AddOrmLite(options => options.UsePostgres(configuredConnection));
                    services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(configuredConnection));
                    break;
                case "sqlserver":
                    services.AddOrmLite(options => options.UseSqlServer(configuredConnection));
                    services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(configuredConnection));
                    break;
                case "mysql":
                    services.AddOrmLite(options => options.UseMySql(configuredConnection));
                    services.AddDbContext<ApplicationDbContext>(options => options.UseMySQL(configuredConnection));
                    break;
                default:
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
                    break;
            }

            // Enable built-in Database Admin UI at /admin-ui/database
            services.AddPlugin(new AdminDatabaseFeature());
        });
}
