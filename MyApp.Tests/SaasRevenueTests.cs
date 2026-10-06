using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// Recurring revenue is estimated from the local projection of subscriptions and the plan catalog's prices, and
/// recorded each day so its history can be charted.
/// </summary>
public class SaasRevenueTests
{
    private TestDatabase database = null!;
    private OrmLiteConnectionFactory factory = null!;

    [SetUp]
    public void SetUp()
    {
        database = TestDatabase.Create();
        factory = database.Factory;
        database.CreateTables(typeof(SaasPlan), typeof(SaasPlanVersion), typeof(SaasPlanPrice), typeof(BillingSubscription),
            typeof(Workspace), typeof(StoredFile), typeof(StripeEventInbox), typeof(SaasDailySnapshot));

        using var db = factory.Open();
        foreach (var (code, monthly, annual) in new[] { ("pro", 4900L, 49000L), ("business", 19900L, 199000L) })
        {
            db.Insert(new SaasPlan { Id = code, Code = code, Name = code.ToTitleCase() });
            db.Insert(new SaasPlanVersion { Id = $"{code}.v1", PlanId = code, Status = PlanVersionStatus.Published });
            db.Insert(new SaasPlanPrice { Id = $"{code}.month", PlanVersionId = $"{code}.v1", UnitAmount = monthly, Interval = BillingInterval.Month });
            db.Insert(new SaasPlanPrice { Id = $"{code}.year", PlanVersionId = $"{code}.v1", UnitAmount = annual, Interval = BillingInterval.Year });
        }
        // A customer kept the Stripe price of a version that's been retired
        db.Insert(new SaasPlanVersion { Id = "pro.v0", PlanId = "pro", Version = 0, Status = PlanVersionStatus.Retired });
        db.Insert(new SaasPlanPrice { Id = "pro.legacy", PlanVersionId = "pro.v0", UnitAmount = 3900, Interval = BillingInterval.Month, StripePriceId = "price_legacy", IsActive = false });

        void Subscription(string id, string version, SubscriptionStatus status, BillingInterval interval = BillingInterval.Month, string? stripePriceId = null) =>
            db.Insert(new BillingSubscription { Id = id, WorkspaceId = id, PlanVersionId = version, Status = status, Interval = interval, StripePriceId = stripePriceId });
        Subscription("pro-active", "pro.v1", SubscriptionStatus.Active);
        Subscription("pro-past-due", "pro.v1", SubscriptionStatus.PastDue);
        Subscription("pro-legacy", "pro.v1", SubscriptionStatus.Active, stripePriceId: "price_legacy");
        Subscription("business-annual", "business.v1", SubscriptionStatus.Active, BillingInterval.Year);
        Subscription("business-trial", "business.v1", SubscriptionStatus.Trialing);
        Subscription("pro-canceled", "pro.v1", SubscriptionStatus.Canceled);
    }

    [TearDown]
    public void TearDown() => database.Dispose();

    [Test]
    public void Billed_subscriptions_are_monthly_revenue_and_trials_are_its_pipeline()
    {
        using var db = factory.Open();

        var revenue = SaasRevenue.Calculate(db, "usd");

        Assert.Multiple(() =>
        {
            // Past due subscriptions are still owed, a Stripe price is used when it's known, and annual prices are a twelfth
            Assert.That(revenue.Plans["pro"].Mrr, Is.EqualTo(49m + 49m + 39m));
            Assert.That(revenue.Plans["business"].Mrr, Is.EqualTo(1990m / 12));
            Assert.That(revenue.Mrr, Is.EqualTo(137m + 1990m / 12));
            Assert.That(revenue.Arr, Is.EqualTo(revenue.Mrr * 12));
            Assert.That(revenue.TrialingMrr, Is.EqualTo(199m));
            Assert.That(revenue.Paying, Is.EqualTo(4));
            Assert.That(revenue.Arpa, Is.EqualTo(Math.Round(revenue.Mrr / 4, 2)));
        });
    }

    [Test]
    public async Task A_Stripe_event_that_changes_what_a_subscription_is_billed_records_its_revenue_change()
    {
        using var db = factory.Open();
        database.CreateTables(typeof(SaasAuditEvent));
        db.UpdateOnly(() => new SaasPlanPrice { StripePriceId = "price_business_month" }, x => x.Id == "business.month");
        db.Insert(new Workspace { Id = "upgrading", Name = "Upgrading", Slug = "upgrading", StripeCustomerId = "cus_upgrading" });
        db.UpdateOnly(() => new BillingSubscription { WorkspaceId = "upgrading", Status = SubscriptionStatus.Trialing }, x => x.Id == "business-trial");
        var workspace = db.SingleById<Workspace>("upgrading");
        db.ForWorkspace(workspace.Id);
        var gateway = new StripeBillingGateway(new StripeConfig(), new SaasConfig(), new ProductConfig());
        StripeEventInbox Event(string id, string status) => new() {
            StripeEventId = id, EventType = "customer.subscription.updated",
            PayloadJson = """
                {"data":{"object":{"id":"sub_upgrading","customer":"cus_upgrading","status":"STATUS",
                  "items":{"data":[{"price":{"id":"price_business_month"}}]}}}}
                """.Replace("STATUS", status),
        };

        // The trial converts, then Stripe sends the same state again
        await gateway.ApplyWebhookAsync(db, workspace, Event("evt_1", "active"));
        await gateway.ApplyWebhookAsync(db, workspace, Event("evt_2", "active"));

        var changes = db.Select<SaasAuditEvent>(x => x.Action == SaasRevenue.ChangeAction);
        Assert.That(changes, Has.Count.EqualTo(1));
        var change = changes[0].DetailJson.FromJson<SubscriptionChange>();
        Assert.Multiple(() =>
        {
            Assert.That(changes[0].WorkspaceId, Is.EqualTo("upgrading"));
            Assert.That(changes[0].UserId, Is.EqualTo("stripe"));
            Assert.That(change.PreviousStatus, Is.EqualTo("Trialing"));
            Assert.That(change.Status, Is.EqualTo("Active"));
            Assert.That(change.PreviousMrr, Is.Zero);
            Assert.That(change.Mrr, Is.EqualTo(199m));
            Assert.That(change.Plan, Is.EqualTo("business"));
        });
    }

    [Test]
    public void Each_months_revenue_movement_comes_from_its_subscription_changes()
    {
        using var db = factory.Open();
        database.CreateTables(typeof(SaasAuditEvent));
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        // The previous month ended with this MRR
        db.Insert(new SaasDailySnapshot { Date = month.AddDays(-1), MetricKey = "revenue.mrr", Value = 1000 });
        void Change(string workspaceId, decimal previousMrr, decimal mrr) => db.Insert(new SaasAuditEvent {
            WorkspaceId = workspaceId, Category = "billing", Action = SaasRevenue.ChangeAction, UserId = "stripe", CreatedDate = now,
            DetailJson = new SubscriptionChange { PreviousMrr = previousMrr, Mrr = mrr, Currency = "usd" }.ToJson(),
        });
        Change("started", 0, 49);
        Change("upgraded", 49, 199);
        Change("downgraded", 199, 49);
        Change("left", 49, 0);

        var revenue = SaasInsights.GetRevenueMetrics(db, "usd", 1, now);

        var current = revenue.Months.Single();
        Assert.Multiple(() =>
        {
            Assert.That(current.StartMrr, Is.EqualTo(1000));
            Assert.That(current.EndMrr, Is.EqualTo(revenue.Mrr));
            Assert.That(current.NewMrr, Is.EqualTo(49));
            Assert.That(current.ExpansionMrr, Is.EqualTo(150));
            Assert.That(current.ContractionMrr, Is.EqualTo(150));
            Assert.That(current.ChurnedMrr, Is.EqualTo(49));
            Assert.That((current.NewCustomers, current.ChurnedCustomers), Is.EqualTo((1, 1)));
            Assert.That(revenue.Series.Last().Date, Is.EqualTo(now.Date));
            // A year of history has a point for each week
            Assert.That(SaasInsights.GetRevenueMetrics(db, "usd", 12, now).Series.Select(x => (now.Date - x.Date).Days % 7), Has.All.Zero);
            Assert.That(revenue.TopCustomers.First().WorkspaceId, Is.EqualTo("business-annual"));
        });
    }

    [Test]
    public void Customer_health_loses_points_for_each_sign_a_customer_might_leave()
    {
        using var db = factory.Open();
        database.CreateTables(typeof(WorkspaceMember), typeof(UsageDailyRollup), typeof(UsagePeriod), typeof(UsageAggregate), typeof(SaasAuditEvent));
        var today = DateTime.UtcNow.Date;
        // Customers for long enough to have a usage trend, inserted without the audit rules that say when they were created
        foreach (var id in new[] { "pro-active", "pro-past-due", "pro-canceled" })
            db.WithoutFilters().Insert(new Workspace { Id = id, Name = id, Slug = id, CreatedDate = today.AddDays(-120) });
        void Usage(string workspaceId, int daysAgo, long units) => db.Insert(new UsageDailyRollup {
            WorkspaceId = workspaceId, Date = today.AddDays(-daysAgo), MeterKey = "api.requests", Units = units,
        });
        Usage("pro-active", 40, 1000);      // the previous 30 days
        Usage("pro-active", 10, 200);       // much less in the last 30, and nothing since
        Usage("pro-past-due", 40, 500);
        Usage("pro-past-due", 2, 500);
        foreach (var daysAgo in new[] { 5, 2 })
            db.Insert(new SaasAuditEvent { WorkspaceId = "pro-past-due", Category = "billing", Action = "invoice.payment_failed", UserId = "stripe", CreatedDate = today.AddDays(-daysAgo) });

        var health = SaasInsights.GetCustomerHealth(db, "usd", DateTime.UtcNow).ToDictionary(x => x.WorkspaceId);

        Assert.Multiple(() =>
        {
            var declining = health["pro-active"];
            Assert.That(declining.Signals.Select(x => (x.Key, x.Impact)), Is.EquivalentTo(new[] { ("usage.inactive", -10), ("usage.declining", -30) }));
            Assert.That((declining.Score, declining.Grade), Is.EqualTo((60, CustomerHealthGrade.Watch)));
            Assert.That(declining.UsageTrendPercent, Is.EqualTo(-80));

            var pastDue = health["pro-past-due"];
            Assert.That(pastDue.Signals.Select(x => x.Key), Is.EquivalentTo(new[] { "billing.past_due", "billing.payment_failed" }));
            Assert.That((pastDue.Score, pastDue.Grade, pastDue.PaymentFailures), Is.EqualTo((50, CustomerHealthGrade.AtRisk, 2)));
            Assert.That(pastDue.Mrr, Is.EqualTo(49));

            Assert.That(health["pro-canceled"].Grade, Is.EqualTo(CustomerHealthGrade.Churned));
            // Customers without an organization aren't customers
            Assert.That(health, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Quota_pressure_is_an_active_customer_near_a_limit_it_can_grow_out_of()
    {
        using var db = factory.Open();
        database.CreateTables(typeof(UsagePeriod), typeof(UsageAggregate));
        var now = DateTime.UtcNow;
        void Usage(string workspaceId, string meterKey, long allowance, long used, int endedDaysAgo = -10)
        {
            var period = new UsagePeriod {
                Id = $"{workspaceId}.{meterKey}.{endedDaysAgo}", WorkspaceId = workspaceId, MeterKey = meterKey, Allowance = allowance,
                PeriodStart = now.AddDays(-endedDaysAgo - 30), PeriodEnd = now.AddDays(-endedDaysAgo),
            };
            db.Insert(period);
            db.Insert(new UsageAggregate { WorkspaceId = workspaceId, UsagePeriodId = period.Id, UsedUnits = used });
        }
        Usage("pro-active", "api.requests", 100_000, 87_000);        // near its limit
        Usage("pro-past-due", "workspace.seats", 1, 1);              // a single-user plan's one seat
        Usage("business-annual", "workspace.seats", 1, 3);           // more members than seats
        Usage("pro-legacy", "api.requests", 100_000, 99_000, endedDaysAgo: 5);  // a period that's over
        Usage("pro-canceled", "documents.stored", 100, 5_000);       // a customer who left, measured against Free

        Assert.That(SaasInsights.QuotaPressure(db, now, take: 25), Is.EquivalentTo(new[] { "pro-active", "business-annual" }));
    }

    [Test]
    public async Task The_daily_snapshot_records_revenue_overall_and_for_each_plan()
    {
        var command = new BuildSaasDailySnapshotCommand(factory, new SaasConfig());

        await command.ExecuteAsync(new NoArgs());
        await command.ExecuteAsync(new NoArgs());   // recording it again updates today's values

        using var db = factory.Open();
        var today = db.Select<SaasDailySnapshot>(x => x.Date == DateTime.UtcNow.Date)
            .ToDictionary(x => (x.MetricKey, x.Dimension), x => x.Value);
        Assert.Multiple(() =>
        {
            Assert.That(today[("revenue.mrr", "all")], Is.EqualTo(302.83m));   // recorded in cents
            Assert.That(today[("revenue.mrr", SaasRevenue.PlanDimension("pro"))], Is.EqualTo(137m));
            Assert.That(today[("revenue.mrr", SaasRevenue.PlanDimension("business"))], Is.EqualTo(165.83m));
            Assert.That(today[("revenue.trialing_mrr", "all")], Is.EqualTo(199m));
            Assert.That(today[("subscriptions.paying", "all")], Is.EqualTo(4));
            Assert.That(today[("subscriptions.canceled", "all")], Is.EqualTo(1));
            Assert.That(db.Count<SaasDailySnapshot>(), Is.EqualTo(today.Count));
        });
    }
}
