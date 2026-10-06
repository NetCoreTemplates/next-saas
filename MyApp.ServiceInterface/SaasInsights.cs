using System.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

/// <summary>
/// What platform operators learn from every customer's data: how recurring revenue moved, and how healthy each
/// customer is. The connection works across organizations; the APIs that call it decide who may see what.
/// </summary>
public static class SaasInsights
{
    /// <summary>
    /// Recurring revenue now, each day's history from the daily snapshots, and each month's movement from the
    /// subscription.changed audit events
    /// </summary>
    public static GetRevenueMetricsResponse GetRevenueMetrics(IDbConnection db, string currency, int months, DateTime now)
    {
        var today = now.Date;
        var firstMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));
        var revenue = SaasRevenue.Calculate(db, currency);
        var response = new GetRevenueMetricsResponse {
            Currency = currency, Mrr = Round(revenue.Mrr), Arr = Round(revenue.Arr), TrialingMrr = Round(revenue.TrialingMrr),
            Arpa = revenue.Arpa, Paying = revenue.Paying,
            Plans = revenue.Plans.Values.OrderByDescending(x => x.Mrr).Select(x => new PlanRevenueInfo {
                PlanCode = x.PlanCode, PlanName = x.PlanName, Subscriptions = x.Subscriptions, Mrr = Round(x.Mrr),
                Percent = revenue.Mrr == 0 ? 0 : Math.Round(x.Mrr * 100 / revenue.Mrr, 1),
            }).ToList(),
        };

        // Each day's snapshot, and today's from what's billed now
        string[] metrics = ["revenue.mrr", "revenue.trialing_mrr", "subscriptions.paying"];
        var snapshots = db.Select<SaasDailySnapshot>(x => x.Date >= firstMonth.AddDays(-1) && x.Date < today
            && x.Dimension == "all" && Sql.In(x.MetricKey, metrics));
        var series = snapshots.GroupBy(x => x.Date.Date).OrderBy(x => x.Key).Select(day => new RevenuePoint {
            Date = day.Key,
            Mrr = day.FirstOrDefault(x => x.MetricKey == "revenue.mrr")?.Value ?? 0,
            TrialingMrr = day.FirstOrDefault(x => x.MetricKey == "revenue.trialing_mrr")?.Value ?? 0,
            Paying = (long)(day.FirstOrDefault(x => x.MetricKey == "subscriptions.paying")?.Value ?? 0),
        }).ToList();
        series.Add(new RevenuePoint { Date = today, Mrr = response.Mrr, TrialingMrr = response.TrialingMrr, Paying = revenue.Paying });
        // A long history has a point for each week, so the response stays small enough for an assistant to read
        response.Series = series.Where(x => x.Date >= firstMonth && (months <= 3 || (today - x.Date).Days % 7 == 0)).ToList();

        var changes = db.Select<SaasAuditEvent>(x => x.Category == "billing" && x.Action == SaasRevenue.ChangeAction && x.CreatedDate >= firstMonth)
            .Select(x => (x.CreatedDate, Change: x.DetailJson?.FromJson<SubscriptionChange>()))
            .Where(x => x.Change != null && (x.Change.Currency.IsNullOrEmpty() || x.Change.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        for (var month = firstMonth; month <= today; month = month.AddMonths(1))
        {
            var next = month.AddMonths(1);
            // A month starts with the MRR it ended the previous one with
            var start = series.LastOrDefault(x => x.Date < month) ?? series.FirstOrDefault(x => x.Date >= month && x.Date < next);
            var end = series.LastOrDefault(x => x.Date < next);
            var item = new RevenueMonth { Month = month, StartMrr = start?.Mrr ?? 0, EndMrr = end?.Mrr ?? 0 };
            foreach (var (_, change) in changes.Where(x => x.CreatedDate >= month && x.CreatedDate < next))
            {
                var delta = change!.Mrr - change.PreviousMrr;
                if (change.PreviousMrr == 0 && change.Mrr > 0)
                {
                    item.NewMrr += delta;
                    item.NewCustomers++;
                }
                else if (change.PreviousMrr > 0 && change.Mrr == 0)
                {
                    item.ChurnedMrr -= delta;
                    item.ChurnedCustomers++;
                }
                else if (delta > 0) item.ExpansionMrr += delta;
                else item.ContractionMrr -= delta;
            }
            item.GrowthPercent = item.StartMrr == 0 ? null : Math.Round((item.EndMrr - item.StartMrr) * 100 / item.StartMrr, 1);
            response.Months.Add(item);
        }

        // Only paying subscriptions are read
        var prices = SaasPriceCatalog.Load(db, currency);
        var billed = db.Select<BillingSubscription>(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue)
            .Select(x => (Subscription: x, Mrr: prices.MonthlyAmount(x.PlanVersionId, x.Interval, x.StripePriceId)))
            .Where(x => x.Mrr > 0).OrderByDescending(x => x.Mrr).Take(10).ToList();
        var names = billed.Count == 0 ? [] : db.Dictionary<string, string>(db.From<Workspace>()
            .Where(x => Sql.In(x.Id, billed.Select(y => y.Subscription.WorkspaceId))).Select(x => new { x.Id, x.Name }));
        response.TopCustomers = billed.Select(x => new CustomerRevenueInfo {
            WorkspaceId = x.Subscription.WorkspaceId, Name = names.GetValueOrDefault(x.Subscription.WorkspaceId, ""),
            PlanName = prices.PlanOf(x.Subscription.PlanVersionId).Name, Status = x.Subscription.Status, Mrr = Round(x.Mrr),
        }).ToList();
        return response;
    }

    /// <summary>
    /// Customers that have used at least 80% of an allowance in a period that's still in effect. Customers who
    /// canceled aren't included, as their data is measured against the Free plan they fall back to, nor is a
    /// single seat that's taken, which is all a single-user plan has.
    /// </summary>
    public static List<string> QuotaPressure(IDbConnection db, DateTime now, int take)
    {
        var canceled = db.From<BillingSubscription>().Where(x => x.Status == SubscriptionStatus.Canceled).Select(x => x.WorkspaceId);
        return db.ColumnDistinct<string>(db.From<UsageAggregate>()
            .Join<UsagePeriod>((aggregate, period) => aggregate.UsagePeriodId == period.Id)
            .Where<UsageAggregate, UsagePeriod>((aggregate, period) => period.Allowance > 0 && aggregate.UsedUnits * 100 >= period.Allowance * 80
                && period.PeriodStart <= now && period.PeriodEnd > now
                && !(period.MeterKey == "workspace.seats" && period.Allowance <= 1 && aggregate.UsedUnits <= period.Allowance)
                && !Sql.In(aggregate.WorkspaceId, canceled))
            .Select(x => x.WorkspaceId)).Take(take).ToList();
    }

    /// <summary>
    /// Every customer's health, least healthy first. Each signal that changes its score says why.
    /// </summary>
    public static List<CustomerHealthInfo> GetCustomerHealth(IDbConnection db, string currency, DateTime now)
    {
        var today = now.Date;
        var workspaces = db.Select<Workspace>(x => x.Status != WorkspaceStatus.Deleted);
        var subscriptions = db.Select<BillingSubscription>().ToDictionary(x => x.WorkspaceId);
        var prices = SaasPriceCatalog.Load(db, currency);

        // Each signal is counted and grouped by the database
        var members = db.Dictionary<string, int>(db.From<WorkspaceMember>()
            .Where(x => x.Status == WorkspaceMemberStatus.Active)
            .GroupBy(x => x.WorkspaceId).Select(x => new { x.WorkspaceId, Count = Sql.Count("*") }));
        Dictionary<string, long> ApiRequests(DateTime from, DateTime to) => db.Dictionary<string, long>(db.From<UsageDailyRollup>()
            .Where(x => x.MeterKey == "api.requests" && x.DimensionType == "workspace" && x.Date >= from && x.Date < to)
            .GroupBy(x => x.WorkspaceId).Select(x => new { x.WorkspaceId, Units = Sql.Sum(x.Units) }));
        var recent = ApiRequests(today.AddDays(-29), today.AddDays(1));
        var previous = ApiRequests(today.AddDays(-59), today.AddDays(-29));
        var lastActive = db.Dictionary<string, DateTime>(db.From<UsageDailyRollup>()
            .Where(x => x.DimensionType == "workspace")
            .GroupBy(x => x.WorkspaceId).Select(x => new { x.WorkspaceId, Date = Sql.Max(x.Date) }));
        var allowanceUsed = db.Select<(string WorkspaceId, long UsedUnits, long Allowance)>(db.From<UsageAggregate>()
                .Join<UsagePeriod>((aggregate, period) => aggregate.UsagePeriodId == period.Id)
                .Where<UsageAggregate, UsagePeriod>((aggregate, period) => period.MeterKey == "api.requests" && period.Allowance > 0
                    && period.PeriodStart <= now && period.PeriodEnd > now)
                .Select<UsageAggregate, UsagePeriod>((aggregate, period) => new { aggregate.WorkspaceId, aggregate.UsedUnits, period.Allowance }))
            .GroupBy(x => x.WorkspaceId).ToDictionary(x => x.Key, x => x.Max(y => y.UsedUnits * 100m / y.Allowance));
        var paymentFailures = db.Dictionary<string, int>(db.From<SaasAuditEvent>()
            .Where(x => x.Category == "billing" && x.Action == "invoice.payment_failed" && x.CreatedDate >= now.AddDays(-90))
            .GroupBy(x => x.WorkspaceId).Select(x => new { x.WorkspaceId, Count = Sql.Count("*") }));

        var results = new List<CustomerHealthInfo>();
        foreach (var workspace in workspaces)
        {
            if (!subscriptions.TryGetValue(workspace.Id, out var subscription)) continue;
            var (planCode, planName) = prices.PlanOf(subscription.PlanVersionId);
            var health = new CustomerHealthInfo {
                WorkspaceId = workspace.Id, Name = workspace.Name, Slug = workspace.Slug, Kind = workspace.Kind,
                PlanCode = planCode, PlanName = planName, Status = subscription.Status, CreatedDate = workspace.CreatedDate,
                Mrr = SaasRevenue.IsBilled(subscription.Status)
                    ? Round(prices.MonthlyAmount(subscription.PlanVersionId, subscription.Interval, subscription.StripePriceId)) : 0,
                Members = members.GetValueOrDefault(workspace.Id),
                ApiRequests = recent.GetValueOrDefault(workspace.Id),
                PreviousApiRequests = previous.GetValueOrDefault(workspace.Id),
                ApiAllowanceUsedPercent = allowanceUsed.TryGetValue(workspace.Id, out var used) ? Math.Round(used, 1) : null,
                LastActiveDate = lastActive.TryGetValue(workspace.Id, out var active) ? active : null,
                PaymentFailures = paymentFailures.GetValueOrDefault(workspace.Id),
            };
            Score(health, subscription, today);
            results.Add(health);
        }
        return results.OrderBy(x => x.Grade == CustomerHealthGrade.Churned).ThenBy(x => x.Score).ThenByDescending(x => x.Mrr).ToList();
    }

    /// <summary>
    /// A customer starts at 100 and loses points for each sign that it might leave
    /// </summary>
    private static void Score(CustomerHealthInfo health, BillingSubscription subscription, DateTime today)
    {
        var score = 100;
        void Signal(string key, string label, int impact = 0)
        {
            health.Signals.Add(new HealthSignal { Key = key, Label = label, Impact = impact });
            score += impact;
        }

        if (subscription.Status == SubscriptionStatus.Canceled)
        {
            Signal("billing.canceled", subscription.CancelAt is { } canceled
                ? $"Subscription was canceled {(today - canceled.Date).Days} days ago" : "Subscription was canceled", -100);
            health.Score = 0;
            health.Grade = CustomerHealthGrade.Churned;
            return;
        }
        if (subscription.Status == SubscriptionStatus.PastDue) Signal("billing.past_due", "Payment is past due", -30);
        if (subscription.Status == SubscriptionStatus.Paused) Signal("billing.paused", "Subscription is paused", -20);
        if (subscription.Status == SubscriptionStatus.Trialing && subscription.TrialEnd is { } trialEnd)
            Signal("billing.trial", $"Trial ends in {Math.Max(0, (trialEnd.Date - today).Days)} days");
        if (health.PaymentFailures > 0)
            Signal("billing.payment_failed", $"{health.PaymentFailures} failed payment{(health.PaymentFailures == 1 ? "" : "s")} in 90 days",
                -Math.Min(20, 10 * health.PaymentFailures));

        var inactiveDays = health.LastActiveDate is { } lastActive ? (today - lastActive.Date).Days : (int?)null;
        if (inactiveDays == null) Signal("usage.none", "No usage recorded", -25);
        else if (inactiveDays >= 14) Signal("usage.inactive", $"No usage in {inactiveDays} days", -25);
        else if (inactiveDays >= 7) Signal("usage.inactive", $"No usage in {inactiveDays} days", -10);

        // Trends need two full windows of history, and enough usage to not be noise
        if ((today - health.CreatedDate.Date).Days >= 60 && health.PreviousApiRequests >= 100)
        {
            var trend = Math.Round((health.ApiRequests - health.PreviousApiRequests) * 100m / health.PreviousApiRequests, 1);
            health.UsageTrendPercent = trend;
            if (trend <= -50) Signal("usage.declining", $"API requests are down {-trend:0}% on the previous 30 days", -30);
            else if (trend <= -25) Signal("usage.declining", $"API requests are down {-trend:0}% on the previous 30 days", -15);
            else if (trend >= 25)
            {
                Signal("usage.growing", $"API requests are up {trend:0}% on the previous 30 days");
                health.ExpansionCandidate = true;
            }
        }

        if (health.ApiAllowanceUsedPercent is { } allowanceUsed && allowanceUsed >= 75)
        {
            Signal("usage.quota", $"Has used {allowanceUsed:0}% of this period's API allowance", allowanceUsed >= 90 ? -10 : 0);
            health.ExpansionCandidate = true;
        }

        health.Score = Math.Clamp(score, 0, 100);
        health.Grade = health.Score >= 80 ? CustomerHealthGrade.Healthy
            : health.Score >= 55 ? CustomerHealthGrade.Watch
            : CustomerHealthGrade.AtRisk;
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2);
}
