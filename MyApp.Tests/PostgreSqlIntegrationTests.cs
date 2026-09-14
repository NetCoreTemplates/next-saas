using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using Npgsql;
using NUnit.Framework;
using System.Net;
using ServiceStack;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.PostgreSQL;

namespace MyApp.Tests;

[TestFixture]
public class PostgreSqlIntegrationTests
{
    [Test]
    public async Task Concurrent_quota_admission_does_not_oversubscribe_a_postgresql_aggregate()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (configured.IsNullOrEmpty())
            Assert.Ignore("Set TEST_POSTGRES_CONNECTION to run PostgreSQL integration tests.");

        var schema = "next_saas_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured!);
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();

        try
        {
            var testBuilder = new NpgsqlConnectionStringBuilder(configured!) { SearchPath = schema };
            var factory = new OrmLiteConnectionFactory(testBuilder.ConnectionString, PostgreSqlDialect.Provider);
            Seed(factory);
            var manager = new SaasManager(new SaasConfig());
            const int attempts = 24;
            var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(async i =>
            {
                await Task.Yield();
                using var db = factory.Open();
                var workspace = db.SingleById<Workspace>("workspace-1");
                var subscription = db.SingleById<BillingSubscription>("subscription-1");
                try
                {
                    manager.RecordUsage(db, workspace, subscription, $"user-{i}", new RecordUsage {
                        MeterKey = "api.requests", Units = 1, IdempotencyKey = $"request-{i}",
                    });
                    return true;
                }
                catch (HttpError error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return false;
                }
            }));

            using var verify = factory.Open();
            var aggregate = verify.Select<UsageAggregate>().Single();
            Assert.Multiple(() => {
                Assert.That(results.Count(x => x), Is.EqualTo(10));
                Assert.That(results.Count(x => !x), Is.EqualTo(attempts - 10));
                Assert.That(aggregate.UsedUnits, Is.EqualTo(10));
                Assert.That(verify.Count<UsageEvent>(), Is.EqualTo(10));
            });
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static void Seed(OrmLiteConnectionFactory factory)
    {
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<SaasPlanPrice>();
        db.CreateTable<SaasPlanFeature>();
        db.CreateTable<SaasPlanQuota>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<UsagePeriod>();
        db.CreateTable<UsageAggregate>();
        db.CreateTable<UsageEvent>();
        db.CreateTable<CustomerEntitlementOverride>();
        db.CreateTable<SaasAuditEvent>();

        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Insert(new Workspace { Id = "workspace-1", Name = "PostgreSQL test", Slug = schemaSlug(), CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlan { Id = "plan.test", Code = "test", Name = "Test", CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanVersion { Id = "plan.test.v1", PlanId = "plan.test", Version = 1, Status = PlanVersionStatus.Published, CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanQuota { Id = "quota.test.api", PlanVersionId = "plan.test.v1", MeterKey = "api.requests", DisplayName = "API requests", IncludedUnits = 10, Enforcement = QuotaEnforcement.HardLimit, CreatedDate = now, ModifiedDate = now });
        db.Insert(new BillingSubscription { Id = "subscription-1", WorkspaceId = "workspace-1", PlanVersionId = "plan.test.v1", Status = SubscriptionStatus.Active, PeriodStart = periodStart, PeriodEnd = periodStart.AddMonths(1), CreatedDate = now, ModifiedDate = now });
        new SaasManager(new SaasConfig()).GetUsage(db,
            db.SingleById<Workspace>("workspace-1"), db.SingleById<BillingSubscription>("subscription-1"));

        static string schemaSlug() => "postgresql-test-" + Guid.NewGuid().ToString("N")[..8];
    }
}
