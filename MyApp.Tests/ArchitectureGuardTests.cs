using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MyApp.Tests;

public class ArchitectureGuardTests
{
    [Test]
    public void Every_OrmLite_table_is_classified_in_the_feature_manifest()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "MyApp", "Migrations", "Migration1000.cs"));
        var createdTables = Regex.Matches(migration, @"CreateTable<(?<name>[^>]+)>")
            .Select(x => x.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "features.json")));
        var classifications = manifest.RootElement.GetProperty("tableClassification");
        var classified = classifications.EnumerateObject()
            .SelectMany(x => x.Value.EnumerateArray())
            .Select(x => x.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(createdTables.Except(classified), Is.Empty,
            "New tables must be classified as global, tenant-owned, or platform-operational in features.json.");
        Assert.That(classified.Except(createdTables), Is.Empty,
            "Remove stale table classifications when consolidating development migrations.");
    }

    [Test]
    public void Feature_manifest_has_unique_module_names()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "features.json")));
        var names = manifest.RootElement.GetProperty("modules").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()!).ToList();
        Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(names.Count));
    }

    [Test]
    public void Audit_events_cannot_bypass_the_policy_with_a_generic_insert()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.EndsWith("SaasAudit.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Insert<" + "SaasAuditEvent>", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "Use db.Insert(new SaasAuditEvent { ... }) or SaasAudit.Write so validation and redaction cannot be bypassed.");
    }
}
