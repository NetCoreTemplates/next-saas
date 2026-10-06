using System.Text.Json;
using System.Text.RegularExpressions;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;

namespace MyApp.Tests;

public class ArchitectureGuardTests
{
    [Test]
    public void Feature_manifest_has_unique_module_names()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "features.json")));
        var names = manifest.RootElement.GetProperty("modules").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()!).ToList();
        Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(names.Count));
    }

    [Test]
    public void Tenant_owned_tables_are_confined_to_their_organization()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "features.json")));
        var tenantOwned = manifest.RootElement.GetProperty("tableClassification").GetProperty("tenantOwned")
            .EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
        var confined = typeof(IHasWorkspaceId).Assembly.GetTypes()
            .Where(x => x is { IsClass: true, IsAbstract: false } && typeof(IHasWorkspaceId).IsAssignableFrom(x))
            .Select(x => x.Name)
            // Workspace is confined by its Id, see SaasDb.ForWorkspace()
            .Append(nameof(Workspace))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(tenantOwned.Except(confined), Is.Empty,
            "Tables classified as tenantOwned in features.json must implement IHasWorkspaceId so connections confine them.");
        Assert.That(confined.Except(tenantOwned), Is.Empty,
            "Tables implementing IHasWorkspaceId must be classified as tenantOwned in features.json.");
    }

    [Test]
    public void Tenant_owned_tables_have_an_index_that_starts_with_their_organization()
    {
        static bool LeadsWithWorkspaceId(Type table)
        {
            var column = table.GetProperty(nameof(IHasWorkspaceId.WorkspaceId))!;
            return column.IsDefined(typeof(ServiceStack.DataAnnotations.PrimaryKeyAttribute), true) ||
                   column.IsDefined(typeof(ServiceStack.DataAnnotations.IndexAttribute), true) ||
                   column.IsDefined(typeof(ServiceStack.DataAnnotations.UniqueAttribute), true) ||
                   table.GetCustomAttributes(typeof(ServiceStack.DataAnnotations.UniqueConstraintAttribute), true)
                       .Cast<ServiceStack.DataAnnotations.UniqueConstraintAttribute>().Any(x => x.FieldNames.FirstOrDefault() == column.Name) ||
                   table.GetCustomAttributes(typeof(ServiceStack.DataAnnotations.CompositeIndexAttribute), true)
                       .Cast<ServiceStack.DataAnnotations.CompositeIndexAttribute>().Any(x => x.FieldNames.FirstOrDefault() == column.Name);
        }

        var offenders = typeof(IHasWorkspaceId).Assembly.GetTypes()
            .Where(x => x is { IsClass: true, IsAbstract: false } && typeof(IHasWorkspaceId).IsAssignableFrom(x))
            .Where(x => !LeadsWithWorkspaceId(x))
            .Select(x => x.Name)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "Every query on a tenant-owned table is filtered by WorkspaceId, so it needs an index, unique constraint " +
            "or composite index that starts with WorkspaceId.");
    }

    [Test]
    public void Only_reviewed_apis_can_be_called_with_an_api_key()
    {
        // An API key authenticates as its user, so each API it can call is a decision. Review a new one before adding it.
        var allowed = new Dictionary<string, string> {
            [nameof(RecordUsage)] = "usage:write",
            [nameof(GetUsageAnalytics)] = "usage:read",
            [nameof(ExportUsageCsv)] = "usage:read",
            [nameof(GetSaasDashboard)] = "workspace:read",
            [nameof(GetEffectiveEntitlements)] = "workspace:read",
        };

        var actual = typeof(IRequireWorkspace).Assembly.GetTypes()
            .Select(x => (Type: x, Scope: x.GetCustomAttributes(typeof(ValidateHasScopeAttribute), true).Cast<ValidateHasScopeAttribute>().FirstOrDefault()?.Scope))
            .Where(x => x.Scope != null)
            .ToDictionary(x => x.Type.Name, x => x.Scope!);

        Assert.That(actual, Is.EquivalentTo(allowed));

        // A request sent with an API key is for the key's organization, which is checked for APIs that are for one
        var notForAnOrganization = typeof(IRequireWorkspace).Assembly.GetTypes()
            .Where(x => x.IsDefined(typeof(ValidateHasScopeAttribute), true) && !typeof(IRequireWorkspace).IsAssignableFrom(x))
            .Select(x => x.Name);
        Assert.That(notForAnOrganization, Is.Empty, "APIs that API keys can call need to implement IRequireWorkspace.");
    }

    [Test]
    public void Apis_for_an_organization_say_which_organization_in_their_request()
    {
        // Platform APIs take the customer an operator is working on, which isn't checked against their membership
        var requestsWithWorkspaceId = typeof(IRequireWorkspace).Assembly.GetTypes()
            .Where(x => x is { IsClass: true, IsAbstract: false } && (typeof(IReturn).IsAssignableFrom(x) || typeof(IReturnVoid).IsAssignableFrom(x)))
            .Where(x => x.GetProperty(nameof(IRequireWorkspace.WorkspaceId)) != null)
            .ToList();

        // Chooses the organization new browser tabs start in, and checks the user is a member of it itself
        string[] accountLevel = [nameof(SwitchWorkspace)];

        var offenders = requestsWithWorkspaceId
            .Where(x => !typeof(IRequireWorkspace).IsAssignableFrom(x) && !accountLevel.Contains(x.Name))
            .Where(x => x.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().All(r => !r.Path.StartsWith("/saas/admin")))
            .Select(x => x.Name).ToList();

        Assert.That(offenders, Is.Empty,
            "Implement IRequireWorkspace so the user is checked to be a member of the organization before the API runs.");
        Assert.That(requestsWithWorkspaceId.Count(x => typeof(IRequireWorkspace).IsAssignableFrom(x)), Is.GreaterThan(25));
    }

    [Test]
    public void Tables_with_audit_columns_use_the_audit_base_class()
    {
        var offenders = typeof(SaasAuditBase).Assembly.GetTypes()
            .Where(x => x is { IsClass: true, IsAbstract: false } && !typeof(SaasAuditBase).IsAssignableFrom(x))
            .Where(x => x.GetProperty(nameof(SaasAuditBase.CreatedBy)) != null || x.GetProperty(nameof(SaasAuditBase.ModifiedBy)) != null)
            .Select(x => x.Name)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "Derive from SaasAuditBase so the connection sets the audit columns, see SaasDb.SetUserId().");
    }

    // The tests below read the source code, for rules that can't be checked from the compiled types.
    // Each limits code that isn't protected by the connection to files that have been reviewed.

    [Test]
    public void Connections_are_opened_for_an_organization_or_across_them()
    {
        // Connections that aren't opened for a request say whether they're confined to an organization
        AssertOnlyIn(@"Factory(>\(\))?\.Open(Async|DbConnection\w*)?\(", [
                "MyApp.ServiceInterface/SaasDb.cs",     // defines OpenForWorkspace() and OpenAcrossWorkspaces()
                "MyApp/Configure.Db.Migrations.cs",     // creates the schema, before any organization exists
                "MyApp/Configure.HealthChecks.cs",      // SELECT 1 and checks the tables exist
            ],
            "A connection opened with dbFactory.Open() isn't confined to an organization. Open it with " +
            "dbFactory.OpenForWorkspace(workspaceId, userId), or dbFactory.OpenAcrossWorkspaces(userId) for " +
            "code that works on more than one.");
    }

    [Test]
    public void Working_across_organizations_is_limited_to_reviewed_files()
    {
        // Code that isn't confined to one organization. Review each new use before adding its file.
        AssertOnlyIn(AcrossOrganizations, [
                "MyApp.ServiceInterface/SaasDb.cs",                 // defines AcrossWorkspaces() and OpenAcrossWorkspaces()
                "MyApp.ServiceInterface/SaasServices.cs",           // account-level APIs, plan administration, Stripe events
                "MyApp.ServiceInterface/SaasPlatformServices.cs",   // resolving the organization, platform APIs
                "MyApp.ServiceInterface/RegisterService.cs",        // creates the new user's organization
                "MyApp.ServiceInterface/SaasMaintenanceJobs.cs",    // scheduled jobs that sweep every organization
                "MyApp.ServiceInterface/AccountDeletionManager.cs", // removes a user from every organization they belong to
                "MyApp/Configure.ApiKeys.cs",                       // creates the API Keys table at startup
                "MyApp/ExampleDataSeeder.cs",                       // seeds several organizations with historic dates
            ],
            "AcrossWorkspaces(), OpenAcrossWorkspaces() and WithoutFilters() bypass tenant isolation. Prefer a " +
            "connection that's confined to the organization, or review the new use and add its file to this allow-list.");
    }

    // Code that works across organizations, which can't rely on a confined connection
    private const string AcrossOrganizations = @"AcrossWorkspaces\(|WithoutFilters\(";

    [Test]
    public void Only_code_that_works_across_organizations_writes_their_condition()
    {
        // A connection confined to an organization adds its condition to every query, so code that's confined
        // doesn't write it. Writing it anyway would hide a connection that isn't confined, which instead fails closed.
        var acrossOrganizations = Sources.Value.Where(x => Regex.IsMatch(x.Value, AcrossOrganizations)).Select(x => x.Key);
        var withConditions = Sources.Value.Where(x => Regex.IsMatch(x.Value, @"\.WorkspaceId\s*[!=]=")).Select(x => x.Key);
        Assert.That(withConditions.Except(acrossOrganizations), Is.Empty,
            "Remove the WorkspaceId condition from queries on a connection confined to the organization. Code that " +
            "works across organizations writes it, and is limited to reviewed files.");
    }

    [Test]
    public void Raw_sql_is_limited_to_reviewed_files()
    {
        // Connection filters and rules don't apply to complete SQL statements, which need their own conditions
        AssertOnlyIn(@"ExecuteSql(Async)?\(|Sql(List|Scalar|Column)(Async)?<|ExecuteNonQuery\(", [
                "MyApp.ServiceInterface/SaasServices.cs",   // atomic usage counters, which include the WorkspaceId
                "MyApp/Configure.HealthChecks.cs",          // SELECT 1
            ],
            "Raw SQL isn't confined to the organization or given audit columns. Prefer the typed APIs, or include " +
            "the WorkspaceId in the statement, review it, and add its file to this allow-list.");

        // Sql.Fmt() sends every interpolated value as a db param, so a value can't be concatenated into the statement
        AssertOnlyIn(@"(ExecuteSql(Async)?|ExecuteNonQuery(Async)?|Sql(List|Scalar|Column)(Async)?<[^>]+>)\((?!\s*Sql\.Fmt\()", [],
            "Raw SQL is written with Sql.Fmt(), e.g. db.ExecuteSql(Sql.Fmt($\"UPDATE {Table} SET {Column} = {value}\")).");
    }

    [Test]
    public void Audit_events_cannot_bypass_the_policy_with_a_generic_insert()
    {
        // Audit events are validated and redacted when they're written
        AssertOnlyIn(@"Insert<(Saas|Platform)AuditEvent>", [],
            "Use db.Insert(new SaasAuditEvent { ... }) or SaasAudit.Write so validation and redaction cannot be bypassed.");
    }

    [Test]
    public void Every_OrmLite_table_is_classified_in_the_feature_manifest()
    {
        var createdTables = Regex.Matches(Sources.Value["MyApp/Migrations/Migration1000.cs"], @"CreateTable<(?<name>[^>]+)>")
            .Select(x => x.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "features.json")));
        var classified = manifest.RootElement.GetProperty("tableClassification").EnumerateObject()
            .SelectMany(x => x.Value.EnumerateArray())
            .Select(x => x.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(createdTables.Except(classified), Is.Empty,
            "New tables must be classified as global, tenant-owned, or platform-operational in features.json.");
        Assert.That(classified.Except(createdTables), Is.Empty,
            "Remove stale table classifications when consolidating development migrations.");
    }

    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    // The App's source files by their path from the repository, e.g. MyApp.ServiceInterface/SaasDb.cs
    private static readonly Lazy<Dictionary<string, string>> Sources = new(() =>
        new[] { "MyApp", "MyApp.ServiceInterface", "MyApp.ServiceModel" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(Root, project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(path => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText));

    private static void AssertOnlyIn(string pattern, string[] allowed, string message)
    {
        var files = Sources.Value.Where(x => Regex.IsMatch(x.Value, pattern)).Select(x => x.Key).ToList();
        Assert.That(files.Except(allowed), Is.Empty, message);
        Assert.That(allowed.Except(files), Is.Empty, $"Remove files that no longer match {pattern} from its allow-list.");
    }
}
