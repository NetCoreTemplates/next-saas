using System.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

/// <summary>
/// Recurring revenue estimated from the local projection of Stripe subscriptions and the plan catalog's prices.
/// It's an estimate: Stripe owns coupons, discounts, taxes and credits, and a negotiated price that isn't in the
/// catalog, e.g. for a contact-sales plan, counts as nothing.
/// </summary>
public static class SaasRevenue
{
    /// <summary>
    /// Subscriptions that are billed. Past due subscriptions are still owed; trials aren't revenue until they convert.
    /// </summary>
    public static bool IsBilled(SubscriptionStatus status) => status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    /// <summary>
    /// The recurring revenue of every subscription. The connection has to work across organizations.
    /// </summary>
    public static RevenueSummary Calculate(IDbConnection db, string currency)
    {
        // Grouped by the database, so the cost doesn't grow with the number of customers
        var groups = db.Select<SubscriptionGroup>(db.From<BillingSubscription>()
            .Where(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue || x.Status == SubscriptionStatus.Trialing)
            .GroupBy(x => new { x.PlanVersionId, x.StripePriceId, x.Interval, x.Status })
            .Select(x => new { x.PlanVersionId, x.StripePriceId, x.Interval, x.Status, Count = Sql.Count("*") }));
        var prices = SaasPriceCatalog.Load(db, currency);

        var summary = new RevenueSummary();
        foreach (var group in groups)
        {
            var (code, name) = prices.PlanOf(group.PlanVersionId);
            summary.Add(code, name, group.Status,
                prices.MonthlyAmount(group.PlanVersionId, group.Interval, group.StripePriceId), group.Count);
        }
        return summary;
    }

    /// <summary>
    /// The revenue metrics recorded in each day's SaasDailySnapshot, in cents. Each plan's MRR has the plan's code
    /// as its dimension.
    /// </summary>
    public static IEnumerable<(string Key, string Dimension, decimal Value)> SnapshotMetrics(RevenueSummary revenue)
    {
        yield return ("revenue.mrr", "all", Cents(revenue.Mrr));
        yield return ("revenue.arr", "all", Cents(revenue.Arr));
        yield return ("revenue.trialing_mrr", "all", Cents(revenue.TrialingMrr));
        yield return ("revenue.arpa", "all", revenue.Arpa);
        yield return ("subscriptions.paying", "all", revenue.Paying);
        foreach (var plan in revenue.Plans.Values)
            yield return ("revenue.mrr", PlanDimension(plan.PlanCode), Cents(plan.Mrr));
    }

    private static decimal Cents(decimal amount) => Math.Round(amount, 2);

    public static string PlanDimension(string planCode) => $"plan:{planCode}";

    public const string ChangeAction = "subscription.changed";

    /// <summary>
    /// Records a change to the subscription's plan or billing status in its organization's audit log, with what it's
    /// billed each month before and after, which is how the movement of recurring revenue is analyzed. The
    /// connection is confined to the organization.
    /// </summary>
    public static void RecordChange(IDbConnection db, SubscriptionState before, BillingSubscription after, string currency)
    {
        var change = ChangeEvent(SaasPriceCatalog.Load(db, currency), currency, after.Id, before, SubscriptionState.Of(after),
            db.GetUserId() ?? "stripe", DateTime.UtcNow);
        if (change != null) db.Insert(change);
    }

    /// <summary>
    /// The audit event for a change to a subscription, or null when nothing it's billed for changed
    /// </summary>
    public static SaasAuditEvent? ChangeEvent(SaasPriceCatalog prices, string currency, string subscriptionId,
        SubscriptionState before, SubscriptionState after, string userId, DateTime date)
    {
        var previousMrr = IsBilled(before.Status) ? prices.MonthlyAmount(before.PlanVersionId, before.Interval, before.StripePriceId) : 0;
        var mrr = IsBilled(after.Status) ? prices.MonthlyAmount(after.PlanVersionId, after.Interval, after.StripePriceId) : 0;
        var (previousPlan, plan) = (prices.PlanOf(before.PlanVersionId).Code, prices.PlanOf(after.PlanVersionId).Code);
        if (previousMrr == mrr && previousPlan == plan && before.Status == after.Status) return null;
        return new SaasAuditEvent {
            Category = "billing", Action = ChangeAction, UserId = userId, SubjectId = subscriptionId,
            DetailJson = new SubscriptionChange {
                PreviousPlan = previousPlan, Plan = plan,
                PreviousStatus = before.Status.ToString(), Status = after.Status.ToString(),
                PreviousMrr = Cents(previousMrr), Mrr = Cents(mrr), Currency = currency,
            }.ToJson(),
            CreatedDate = date,
        };
    }

    private class SubscriptionGroup
    {
        public string PlanVersionId { get; set; } = "";
        public string? StripePriceId { get; set; }
        public BillingInterval Interval { get; set; }
        public SubscriptionStatus Status { get; set; }
        public long Count { get; set; }
    }
}

/// <summary>
/// What a subscription is billed for
/// </summary>
public record SubscriptionState(string PlanVersionId, SubscriptionStatus Status, BillingInterval Interval, string? StripePriceId)
{
    public static SubscriptionState Of(BillingSubscription subscription) =>
        new(subscription.PlanVersionId, subscription.Status, subscription.Interval, subscription.StripePriceId);
}

/// <summary>
/// The detail of a subscription.changed audit event. Amounts are monthly, in the currency's major unit.
/// </summary>
public class SubscriptionChange
{
    public string PreviousPlan { get; set; } = "";
    public string Plan { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal PreviousMrr { get; set; }
    public decimal Mrr { get; set; }
    public string Currency { get; set; } = "";
}

public class RevenueSummary
{
    /// <summary>Monthly recurring revenue of billed subscriptions, in the currency's major unit</summary>
    public decimal Mrr { get; set; }
    public decimal Arr => Mrr * 12;
    /// <summary>What trials would add to MRR if they all converted</summary>
    public decimal TrialingMrr { get; set; }
    /// <summary>Billed subscriptions on a paid price</summary>
    public long Paying { get; set; }
    /// <summary>Average revenue per paying account</summary>
    public decimal Arpa => Paying == 0 ? 0 : Math.Round(Mrr / Paying, 2);
    public Dictionary<string, PlanRevenue> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds subscriptions with this status, each billed this monthly amount
    /// </summary>
    public void Add(string planCode, string planName, SubscriptionStatus status, decimal monthlyAmount, long subscriptions = 1)
    {
        var mrr = monthlyAmount * subscriptions;
        if (status == SubscriptionStatus.Trialing)
        {
            TrialingMrr += mrr;
            return;
        }
        if (!SaasRevenue.IsBilled(status)) return;
        if (!Plans.TryGetValue(planCode, out var plan))
            Plans[planCode] = plan = new PlanRevenue { PlanCode = planCode, PlanName = planName };
        plan.Subscriptions += subscriptions;
        plan.Mrr += mrr;
        Mrr += mrr;
        if (monthlyAmount > 0) Paying += subscriptions;
    }
}

public class PlanRevenue
{
    public string PlanCode { get; set; } = "";
    public string PlanName { get; set; } = "";
    public long Subscriptions { get; set; }
    public decimal Mrr { get; set; }
}

/// <summary>
/// The plan catalog's prices, to say what a subscription is billed each month
/// </summary>
public class SaasPriceCatalog
{
    private readonly Dictionary<string, SaasPlanPrice> byStripeId;
    private readonly Dictionary<(string PlanVersionId, BillingInterval Interval), SaasPlanPrice> byVersion;
    private readonly Dictionary<string, SaasPlanVersion> versions;
    private readonly Dictionary<string, SaasPlan> plans;

    private SaasPriceCatalog(List<SaasPlanPrice> prices, List<SaasPlanVersion> versions, List<SaasPlan> plans, string currency)
    {
        byStripeId = prices.Where(x => !x.StripePriceId.IsNullOrEmpty())
            .GroupBy(x => x.StripePriceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        // A version has one price for each currency and interval
        byVersion = prices.Where(x => x.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => (x.PlanVersionId, x.Interval));
        this.versions = versions.ToDictionary(x => x.Id);
        this.plans = plans.ToDictionary(x => x.Id);
    }

    public static SaasPriceCatalog Load(IDbConnection db, string currency) =>
        new(db.Select<SaasPlanPrice>(), db.Select<SaasPlanVersion>(), db.Select<SaasPlan>(), currency);

    /// <summary>
    /// The subscription's Stripe price when it's in the catalog, otherwise its plan version's price for its interval,
    /// e.g. before the catalog has been provisioned in Stripe
    /// </summary>
    public decimal MonthlyAmount(string planVersionId, BillingInterval interval, string? stripePriceId = null)
    {
        var price = stripePriceId != null && byStripeId.TryGetValue(stripePriceId, out var stripePrice)
            ? stripePrice
            : byVersion.GetValueOrDefault((planVersionId, interval));
        if (price == null) return 0;
        return price.Interval == BillingInterval.Year ? price.UnitAmount / 12m / 100m : price.UnitAmount / 100m;
    }

    public (string Code, string Name) PlanOf(string planVersionId) =>
        versions.TryGetValue(planVersionId, out var version) && plans.TryGetValue(version.PlanId, out var plan)
            ? (plan.Code, plan.Name)
            : ("unknown", "Unknown");
}
