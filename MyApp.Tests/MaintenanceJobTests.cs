using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// Jobs that process every organization do the work in the database or one organization at a time, so their
/// cost and their mistakes are bounded by one organization.
/// </summary>
public class MaintenanceJobTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
    private TestDatabase database = null!;
    private OrmLiteConnectionFactory factory = null!;

    [SetUp]
    public void SetUp()
    {
        database = TestDatabase.Create();
        factory = database.Factory;
        database.CreateTables(typeof(WorkspaceRetentionPolicy), typeof(SaasAuditEvent), typeof(PlatformAuditEvent), typeof(UsageEvent), typeof(UsageDailyRollup));
    }

    [TearDown]
    public void TearDown() => database.Dispose();

    [Test]
    public void Retention_uses_each_organizations_policy_and_the_default_for_everyone_else()
    {
        using var db = factory.Open();
        db.Insert(new WorkspaceRetentionPolicy { WorkspaceId = "extended", AuditRetentionDays = 730 });
        db.Insert(new WorkspaceRetentionPolicy { WorkspaceId = "held", AuditRetentionDays = 1, LegalHold = true });
        foreach (var organization in new[] { "default", "extended", "held" })
        foreach (var ageDays in new[] { 10, 400, 800 })
        {
            db.Insert(new SaasAuditEvent {
                Id = $"{organization}-{ageDays}", WorkspaceId = organization, Category = "workspace", Action = "created",
                CreatedDate = Now.AddDays(-ageDays),
            });
        }

        var purge = new RetentionPurge(db, db.Select<WorkspaceRetentionPolicy>(), Now, batchSize: 100);
        var deleted = purge.Owned<SaasAuditEvent>(x => x.AuditRetentionDays, 365, cutoff => x => x.CreatedDate <= cutoff);

        Assert.That(deleted, Is.EqualTo(3));
        Assert.That(db.Column<string>(db.From<SaasAuditEvent>().Select(x => x.Id)), Is.EquivalentTo(new[] {
            "default-10",
            "extended-10", "extended-400",              // kept for its longer period
            "held-10", "held-400", "held-800",          // a legal hold keeps everything
        }));
    }

    [Test]
    public void Retention_uses_the_default_for_the_platforms_tables()
    {
        using var db = factory.Open();
        foreach (var ageDays in new[] { 10, 400 })
            db.Insert(new PlatformAuditEvent { Id = $"platform-{ageDays}", Category = "plan", Action = "draft.saved", CreatedDate = Now.AddDays(-ageDays) });

        var purge = new RetentionPurge(db, [], Now, batchSize: 100);
        var deleted = purge.Platform<PlatformAuditEvent>(365, cutoff => x => x.CreatedDate <= cutoff);

        Assert.That(deleted, Is.EqualTo(1));
        Assert.That(db.Column<string>(db.From<PlatformAuditEvent>().Select(x => x.Id)), Is.EqualTo(new[] { "platform-10" }));
    }

    [Test]
    public void Retention_deletes_no_more_than_a_batch_each_run()
    {
        using var db = factory.Open();
        db.Insert(new WorkspaceRetentionPolicy { WorkspaceId = "organization-b", AnalyticsRetentionDays = 30 });
        for (var i = 0; i < 6; i++)
        {
            db.Insert(new UsageEvent { WorkspaceId = "organization-a", MeterKey = "api.requests", IdempotencyKey = $"a-{i}", RecordedDate = Now.AddDays(-500) });
            db.Insert(new UsageEvent { WorkspaceId = "organization-b", MeterKey = "api.requests", IdempotencyKey = $"b-{i}", RecordedDate = Now.AddDays(-60) });
        }

        int Purge() => new RetentionPurge(db, db.Select<WorkspaceRetentionPolicy>(), Now, batchSize: 5)
            .Owned<UsageEvent>(x => x.AnalyticsRetentionDays, 365, cutoff => x => x.RecordedDate <= cutoff);

        Assert.That(Purge(), Is.EqualTo(5));
        Assert.That(Purge(), Is.EqualTo(5));
        Assert.That(Purge(), Is.EqualTo(2));
        Assert.That(db.Count<UsageEvent>(), Is.Zero);
    }

    [Test]
    public async Task Usage_rollups_are_built_for_each_organization_on_its_own_connection()
    {
        var today = DateTime.UtcNow.Date.AddHours(1);
        using (var db = factory.Open())
        {
            void Usage(string organization, string user, long units, string key) => db.Insert(new UsageEvent {
                WorkspaceId = organization, MeterKey = "api.requests", Units = units, IdempotencyKey = key, RecordedDate = today, RecordedBy = user,
            });
            Usage("organization-a", "user-1", 5, "1");
            Usage("organization-a", "user-2", 7, "2");
            Usage("organization-b", "user-1", 100, "1");
        }
        var command = new BuildUsageRollupsCommand(factory, new SaasConfig());

        await command.ExecuteAsync(new NoArgs());
        await command.ExecuteAsync(new NoArgs());   // building them again changes nothing

        using var verify = factory.Open();
        var rollups = verify.Select<UsageDailyRollup>();
        Assert.Multiple(() =>
        {
            Assert.That(rollups.Where(x => x.DimensionType == "workspace").ToDictionary(x => x.WorkspaceId, x => x.Units),
                Is.EquivalentTo(new Dictionary<string, long> { ["organization-a"] = 12, ["organization-b"] = 100 }));
            Assert.That(rollups.Where(x => x.DimensionType == "user" && x.WorkspaceId == "organization-a").Select(x => x.Units),
                Is.EquivalentTo(new long[] { 5, 7 }));
            Assert.That(rollups.Count, Is.EqualTo(5));
            Assert.That(rollups.Select(x => x.ModifiedBy), Has.All.EqualTo("usage-rollup"));
        });
    }
}
