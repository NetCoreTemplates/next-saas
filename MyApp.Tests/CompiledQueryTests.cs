using System.Reflection;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// The queries of metered APIs are compiled (SaasQueries), and every organization's connections share their SQL with
/// the organization as a db param, which SaasDb.WorkspaceFilters adds. A change that stops a filter or query reusing its
/// SQL, e.g. a filter whose SQL changes with its values, fails here instead of making every request slower.
/// </summary>
public class CompiledQueryTests
{
    static IEnumerable<(string Name, CompiledQueryInfo Query)> CompiledQueries() =>
        typeof(SaasQueries).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(x => (x.Name, new CompiledQueryInfo(x.GetValue(null)!)));

    // CachedStatements and NotCachedReason of a CompiledQuery<T,...>, which are on its generic base class
    record CompiledQueryInfo(object Query)
    {
        public int CachedStatements => (int)Query.GetType().GetProperty(nameof(CachedStatements))!.GetValue(Query)!;
        public string? NotCachedReason => (string?)Query.GetType().GetProperty(nameof(NotCachedReason))!.GetValue(Query);
    }

    [Test]
    public void Metered_queries_reuse_their_SQL_for_every_organization()
    {
        using var database = TestDatabase.Create();
        database.CreateTables(typeof(Workspace), typeof(WorkspaceMember), typeof(UserWorkspacePreference),
            typeof(SaasPlan), typeof(SaasPlanVersion), typeof(SaasPlanPrice), typeof(SaasPlanFeature),
            typeof(SaasPlanQuota), typeof(BillingSubscription), typeof(UsagePeriod), typeof(UsageAggregate),
            typeof(UsageEvent), typeof(UsageReservation), typeof(CustomerEntitlementOverride), typeof(SaasAuditEvent),
            typeof(PlatformAuditEvent));

        var manager = new SaasManager(new SaasConfig());
        var now = DateTime.UtcNow;
        Workspace[] workspaces;
        using (var db = database.Factory.OpenAcrossWorkspaces("seed"))
        {
            db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
            db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
            db.Insert(new SaasPlanQuota { Id="quota.free.api", PlanVersionId="plan.free.v1", MeterKey="api.requests", DisplayName="API requests", IncludedUnits=1_000, Enforcement=QuotaEnforcement.HardLimit, CreatedDate=now, ModifiedDate=now });
            workspaces = [
                manager.EnsurePersonalWorkspace(db, "user-a", "Ada", "ada@example.com"),
                manager.EnsurePersonalWorkspace(db, "user-b", "Bob", "bob@example.com"),
            ];
        }

        Dictionary<string, int>? statementsAfterFirst = null;
        foreach (var workspace in workspaces)
        {
            var userId = workspace.Id == workspaces[0].Id ? "user-a" : "user-b";
            using (var db = database.Factory.OpenForWorkspace(workspace.Id, userId))
            {
                var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
                var recorded = manager.RecordUsage(db, workspace, subscription, userId,
                    new RecordUsage { MeterKey = "api.requests", Units = 10, IdempotencyKey = "operation-1" });
                var replay = manager.RecordUsage(db, workspace, subscription, userId,
                    new RecordUsage { MeterKey = "api.requests", Units = 10, IdempotencyKey = "operation-1" });
                var reservation = manager.ReserveUsage(db, workspace, subscription, userId, "api.requests", 5, "reservation-1");

                // Each organization only sees its own usage
                Assert.Multiple(() => {
                    Assert.That(recorded.Usage!.UsedUnits, Is.EqualTo(10));
                    Assert.That(replay.Duplicate, Is.True);
                    Assert.That(reservation.WorkspaceId, Is.EqualTo(workspace.Id));
                });
            }

            if (statementsAfterFirst == null)
            {
                statementsAfterFirst = CompiledQueries().ToDictionary(x => x.Name, x => x.Query.CachedStatements);
                continue;
            }

            // The second organization reused the statements of the first
            foreach (var (name, query) in CompiledQueries())
                Assert.That(query.CachedStatements, Is.EqualTo(statementsAfterFirst[name]), name);
        }

        Assert.That(SaasDb.WorkspaceFilters.NotCachedReasons, Is.Empty);
        foreach (var (name, query) in CompiledQueries())
            Assert.That(query.NotCachedReason, Is.Null, name);
        foreach (var name in new[] { nameof(SaasQueries.AggregateOfPeriod), nameof(SaasQueries.PeriodOfMeter),
                     nameof(SaasQueries.UsageEventByKey), nameof(SaasQueries.ReservationByKey) })
            Assert.That(statementsAfterFirst![name], Is.GreaterThan(0), name);
    }
}
