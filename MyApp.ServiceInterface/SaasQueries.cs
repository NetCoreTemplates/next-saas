using MyApp.ServiceModel;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

/// <summary>
/// The queries every metered API runs, compiled so their SQL is generated once. They're run on connections confined
/// to an organization, which add its condition, so they don't say which organization: connections share their
/// statements, with the organization added as a db param by SaasDb.WorkspaceFilters.
/// See https://docs.servicestack.net/ormlite/compiled-queries
/// </summary>
public static class SaasQueries
{
    public static readonly CompiledQuery<UsageAggregate, string> AggregateOfPeriod =
        OrmLiteQuery.Compile<UsageAggregate, string>((q, usagePeriodId) => q
            .Where(x => x.UsagePeriodId == usagePeriodId));

    public static readonly CompiledQuery<UsagePeriod, string, DateTime> PeriodOfMeter =
        OrmLiteQuery.Compile<UsagePeriod, string, DateTime>((q, meterKey, periodStart) => q
            .Where(x => x.MeterKey == meterKey && x.PeriodStart == periodStart));

    public static readonly CompiledQuery<UsageEvent, string> UsageEventByKey =
        OrmLiteQuery.Compile<UsageEvent, string>((q, idempotencyKey) => q
            .Where(x => x.IdempotencyKey == idempotencyKey));

    public static readonly CompiledQuery<UsageReservation, string> ReservationByKey =
        OrmLiteQuery.Compile<UsageReservation, string>((q, idempotencyKey) => q
            .Where(x => x.IdempotencyKey == idempotencyKey));

    public static readonly CompiledQuery<CustomerEntitlementOverride, string, DateTime> ActiveOverride =
        OrmLiteQuery.Compile<CustomerEntitlementOverride, string, DateTime>((q, key, now) => q
            .Where(x => x.Key == key
                && (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidUntil == null || x.ValidUntil > now)));

    public static readonly CompiledQuery<WorkspaceMember> SeatsInUse =
        OrmLiteQuery.Compile<WorkspaceMember>(q => q
            .Where(x => x.Status != WorkspaceMemberStatus.Disabled));

    // The plan of an organization, which isn't owned by one
    public static readonly CompiledQuery<SaasPlanVersion, string> PlanVersionById =
        OrmLiteQuery.Compile<SaasPlanVersion, string>((q, id) => q.Where(x => x.Id == id));

    public static readonly CompiledQuery<SaasPlan, string> PlanById =
        OrmLiteQuery.Compile<SaasPlan, string>((q, id) => q.Where(x => x.Id == id));

    public static readonly CompiledQuery<SaasPlanFeature, string> EnabledFeatures =
        OrmLiteQuery.Compile<SaasPlanFeature, string>((q, planVersionId) => q
            .Where(x => x.PlanVersionId == planVersionId && x.Enabled).OrderBy(x => x.DisplayOrder));

    public static readonly CompiledQuery<SaasPlanQuota, string> Quotas =
        OrmLiteQuery.Compile<SaasPlanQuota, string>((q, planVersionId) => q
            .Where(x => x.PlanVersionId == planVersionId));

    public static readonly CompiledQuery<SaasPlanPrice, string> ActivePrices =
        OrmLiteQuery.Compile<SaasPlanPrice, string>((q, planVersionId) => q
            .Where(x => x.PlanVersionId == planVersionId && x.IsActive));
}
