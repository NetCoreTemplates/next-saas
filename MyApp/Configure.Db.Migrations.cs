using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MyApp.Data;
using MyApp.Migrations;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;

[assembly: HostingStartup(typeof(MyApp.ConfigureDbMigrations))]

namespace MyApp;

// Code-First DB Migrations: https://docs.servicestack.net/ormlite/db-migrations
public class ConfigureDbMigrations : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureAppHost(appHost => {
            var dbFactory = appHost.Resolve<IDbConnectionFactory>();
            var migrator = new Migrator(dbFactory, typeof(Migration1000).Assembly);
            void RunMigrations()
            {
                var log = appHost.GetApplicationServices().GetRequiredService<ILogger<ConfigureDbMigrations>>();

                log.LogInformation("Running EF Migrations...");
                var scopeFactory = appHost.GetApplicationServices().GetRequiredService<IServiceScopeFactory>();
                using (var scope = scopeFactory.CreateScope())
                {
                    using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    // The checked-in Identity migration targets SQLite. Every server provider
                    // is bootstrapped from the current model instead; this template deliberately
                    // supports clean database recreation during active development.
                    if (db.Database.IsSqlite() && db.Database.GetMigrations().Any())
                    {
                        db.Database.Migrate();
                    }
                    else
                    {
                        // EnsureCreated() only creates tables when the database has none, and
                        // ServiceStack features such as API keys and CrudEvents create theirs
                        // while the application starts, which happens before this deployment
                        // task runs. Create the Identity schema explicitly when it is the part
                        // that is missing, rather than relying on the database being untouched.
                        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
                        if (!creator.Exists())
                            creator.Create();
                        using var schemaDb = dbFactory.Open();
                        if (!schemaDb.TableExists("AspNetUsers"))
                            creator.CreateTables();
                    }

                    EnsureRolesAsync(scope.ServiceProvider).GetAwaiter().GetResult();

                    // Never create known demo credentials outside Development.
                    var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
                    if (environment.IsDevelopment() && !db.Users.Any())
                    {
                        log.LogInformation("Adding Seed Users...");
                        AddSeedUsers(scope.ServiceProvider).Wait();
                    }

                }

                log.LogInformation("Running OrmLite Migrations...");
                migrator.Run();

                // Bootstrap only after both schemas exist. A credentials error must
                // never leave readiness looking healthy against an Identity-only DB.
                using var bootstrapScope = scopeFactory.CreateScope();
                EnsureBootstrapAdminAsync(bootstrapScope.ServiceProvider).GetAwaiter().GetResult();
            }
            AppTasks.Register("migrate", _ => RunMigrations());
            AppTasks.Register("migrate.revert", args => migrator.Revert(args[0]));
            AppTasks.Register("migrate.rerun", args => migrator.Rerun(args[0]));
            AppTasks.Run();

            var configuration = appHost.GetApplicationServices().GetRequiredService<IConfiguration>();
            if (configuration.GetValue("Database:AutoMigrateEmpty", true))
            {
                using var db = dbFactory.Open();
                if (!db.TableExists<Workspace>())
                {
                    appHost.GetApplicationServices().GetRequiredService<ILogger<ConfigureDbMigrations>>()
                        .LogInformation("Empty database detected; bootstrapping schemas and reference data...");
                    RunMigrations();
                }
            }
        });

    private static readonly string[] PlatformRoles = ["Admin", "BillingAdmin", "Support"];

    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in PlatformRoles)
        {
            var roleExist = await roleManager.RoleExistsAsync(roleName);
            if (!roleExist)
                AssertResult(await roleManager.CreateAsync(new IdentityRole(roleName)));
        }
    }

    private static async Task EnsureBootstrapAdminAsync(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(password))
            return;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("BootstrapAdmin requires both Email and Password.");

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            var displayName = configuration["BootstrapAdmin:DisplayName"]?.Trim();
            user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                DisplayName = string.IsNullOrEmpty(displayName) ? "Platform Administrator" : displayName,
                EmailConfirmed = true,
                ProfileUrl = SvgCreator.CreateSvgDataUri(char.ToUpper(email[0])),
            };
            AssertResult(await userManager.CreateAsync(user, password));
        }
        if (!await userManager.IsInRoleAsync(user, "Admin"))
            AssertResult(await userManager.AddToRoleAsync(user, "Admin"));

        services.GetRequiredService<ILogger<ConfigureDbMigrations>>()
            .LogInformation("Configured bootstrap platform administrator is ready.");
    }

    private async Task AddSeedUsers(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        async Task EnsureUserAsync(ApplicationUser user, string password, string[]? roles = null)
        {
            var existingUser = await userManager.FindByEmailAsync(user.Email!);
            if (existingUser != null) return;

            AssertResult(await userManager.CreateAsync(user, password));
            if (roles?.Length > 0)
            {
                var newUser = await userManager.FindByEmailAsync(user.Email!);
                AssertResult(await userManager.AddToRolesAsync(newUser!, roles));
            }
        }

        ApplicationUser[] users = [
            new()
            {
                DisplayName = "Test User",
                Email = "test@email.com",
                UserName = "test@email.com",
                FirstName = "Test",
                LastName = "User",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Test Employee",
                Email = "employee@email.com",
                UserName = "employee@email.com",
                FirstName = "Test",
                LastName = "Employee",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Test Manager",
                Email = "manager@email.com",
                UserName = "manager@email.com",
                FirstName = "Test",
                LastName = "Manager",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Admin User",
                Email = "admin@email.com",
                UserName = "admin@email.com",
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Support Operator",
                Email = "support@email.com",
                UserName = "support@email.com",
                FirstName = "Support",
                LastName = "Operator",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Billing Operator",
                Email = "billing@email.com",
                UserName = "billing@email.com",
                FirstName = "Billing",
                LastName = "Operator",
                EmailConfirmed = true,
            },
        ];

        for (int i = 0; i < users.Length; i++)
        {
            var user = users[i];
            user.ProfileUrl ??= SvgCreator.CreateSvgDataUri(char.ToUpper(user.UserName![0]), 
                    bgColor:SvgCreator.GetDarkColor(i));
            var roles = user.UserName switch
            {
                "admin@email.com" => PlatformRoles,
                "support@email.com" => ["Support"],
                "billing@email.com" => ["BillingAdmin"],
                _ => null,
            };
            await EnsureUserAsync(user, "p@55wOrd", roles);
        }
    }

    private static void AssertResult(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Errors.First().Description);
    }
}
