using ServiceStack;
using ServiceStack.DataAnnotations;

namespace MyApp.ServiceModel;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresFeatureAttribute(string featureKey) : Attribute
{
    public string FeatureKey { get; } = featureKey;
}

/// <summary>
/// An API for one organization. The request says which organization it's for. When its database connection
/// is opened the user is checked to be a member of that organization, and the request's connections are
/// confined to it (see SaasDb.ForRequest). An API that uses tables owned by an organization without
/// implementing it fails, as its connection isn't confined.
/// </summary>
public interface IRequireWorkspace
{
    string WorkspaceId { get; set; }
}

/// <summary>
/// What an API needs from its organization, which decides whether it's available while the organization is
/// suspended, read-only or pending deletion.
/// </summary>
public enum WorkspaceAccess
{
    /// <summary>
    /// Administering the organization itself: its dashboard, billing, members, notifications and lifecycle.
    /// Stays available so a customer can see and fix the state their organization is in.
    /// </summary>
    Account,
    /// <summary>Reading product data. Unavailable while the organization is suspended.</summary>
    Read,
    /// <summary>
    /// Changing product data. Unavailable while the organization is suspended, read-only or pending deletion.
    /// </summary>
    Write,
}

/// <summary>
/// The access an <see cref="IRequireWorkspace"/> API needs. Without it GET APIs need Read and others need Write.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class WorkspaceAccessAttribute(WorkspaceAccess access) : Attribute
{
    public WorkspaceAccess Access { get; } = access;
}

public enum MeterKind { Counter, Gauge, ReservableCounter }
public enum MeterReset { BillingPeriod, CalendarMonth, Never }
public enum MeterAggregation { Sum, Current, Maximum }
public enum UsageReservationStatus { Pending, Settled, Released, Expired }
public enum StoredFileStatus { Pending, Available, Deleting, Deleted, Failed }
public enum NotificationChannel { Email, InApp }
public enum NotificationDeliveryStatus { Pending, Sending, Delivered, Failed, Suppressed }
public enum WorkspaceAccessMode { Full, Grace, ReadOnly, FreeFallback, Suspended }
public enum LifecycleRequestType { Export, Delete, CancelDelete, TransferOwnership }
public enum LifecycleRequestStatus { Pending, Scheduled, Processing, Completed, Failed, Canceled, Blocked }
public enum PlatformOperationType { GaugeAdjustment, BillingReconciliation, WorkspaceStatusChange }

[UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
[CompositeIndex(nameof(Status), nameof(ExpiresAt))]
public class UsageReservation : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    [References(typeof(UsagePeriod)), Index] public string UsagePeriodId { get; set; } = "";
    public string MeterKey { get; set; } = "";
    public long ReservedUnits { get; set; }
    public long? SettledUnits { get; set; }
    public UsageReservationStatus Status { get; set; } = UsageReservationStatus.Pending;
    public string IdempotencyKey { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? SettledDate { get; set; }
    public string? MetadataJson { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
[CompositeIndex(nameof(WorkspaceId), nameof(Status), nameof(CreatedDate))]
public class StoredFile : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long ByteLength { get; set; }
    public string? Sha256 { get; set; }
    public StoredFileStatus Status { get; set; } = StoredFileStatus.Pending;
    public string UploadedBy { get; set; } = "";
    public DateTime? DeletedDate { get; set; }
    public string? LastError { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(Date), nameof(MeterKey), nameof(DimensionType), nameof(DimensionValue))]
public class UsageDailyRollup : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    [Index] public DateTime Date { get; set; }
    public string MeterKey { get; set; } = "";
    public long Units { get; set; }
    public long EventCount { get; set; }
    public string DimensionType { get; set; } = "workspace";
    public string DimensionValue { get; set; } = "all";
}

[UniqueConstraint(nameof(Date), nameof(MetricKey), nameof(Dimension))]
public class SaasDailySnapshot : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public DateTime Date { get; set; }
    [Index] public string MetricKey { get; set; } = "";
    public string Dimension { get; set; } = "all";
    public decimal Value { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(UserId), nameof(TemplateKey), nameof(Channel))]
public class NotificationPreference : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public string UserId { get; set; } = "";
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    public string TemplateKey { get; set; } = "*";
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; } = true;
}

[UniqueConstraint(nameof(WorkspaceId), nameof(DeduplicationKey))]
[CompositeIndex(nameof(WorkspaceId), nameof(UserId), nameof(Channel))]
[CompositeIndex(nameof(Status), nameof(ModifiedDate))]
public class NotificationDelivery : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    [Index] public string UserId { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string TemplateKey { get; set; } = "";
    public NotificationChannel Channel { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public string? ProviderId { get; set; }
    public string? LastError { get; set; }
    public DateTime? DeliveredDate { get; set; }
    public DateTime? ReadDate { get; set; }
}

public class SupportNote : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = default!;
    public string Body { get; set; } = "";
}

[CompositeIndex(nameof(WorkspaceId), nameof(OperatorId))]
public class SupportAccessGrant : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    [Index] public string OperatorId { get; set; } = "";
    public string Capability { get; set; } = "ReadOnly";
    public string Reason { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AccessStartedAt { get; set; }
    public DateTime? AccessEndedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class PlatformCapabilitiesInfo
{
    public bool CanViewCustomers { get; set; }
    public bool CanManageBilling { get; set; }
    public bool CanManageSupport { get; set; }
    public bool CanManagePlatform { get; set; }
    public bool CanApproveSupportAccess { get; set; }
}

public class PlatformOperatorInfo
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}

public class SaasCustomerSummary
{
    public string WorkspaceId { get; set; } = default!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public WorkspaceStatus Status { get; set; }
    public string? BillingEmail { get; set; }
    public string PlanName { get; set; } = "";
    public SubscriptionStatus SubscriptionStatus { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? MatchedOn { get; set; }
}

[CompositeIndex(nameof(WorkspaceId), nameof(Type), nameof(Status))]
[CompositeIndex(nameof(Status), nameof(ScheduledAt))]
public class WorkspaceLifecycleRequest : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    public LifecycleRequestType Type { get; set; }
    public LifecycleRequestStatus Status { get; set; } = LifecycleRequestStatus.Pending;
    public string RequestedBy { get; set; } = "";
    public string? TargetUserId { get; set; }
    public string? Confirmation { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public class DataExportArtifact : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(WorkspaceLifecycleRequest)), Index] public string LifecycleRequestId { get; set; } = "";
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = default!;
    public string ObjectKey { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? DownloadedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public string? LastError { get; set; }
}

public class WorkspaceRetentionPolicy : SaasAuditBase, IHasWorkspaceId
{
    [PrimaryKey, References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    public int? AnalyticsRetentionDays { get; set; }
    public int? AuditRetentionDays { get; set; }
    public int? NotificationRetentionDays { get; set; }
    public int? DeletedFileRetentionDays { get; set; }
    public int? LifecycleHistoryRetentionDays { get; set; }
    public bool LegalHold { get; set; }
    public string? Reason { get; set; }
}

public class DataRetentionRun : SaasAuditBase
{
    [AutoIncrement] public long Id { get; set; }
    public string Status { get; set; } = "Running";
    public int UsageEventsDeleted { get; set; }
    public int UsageRollupsDeleted { get; set; }
    public int NotificationsDeleted { get; set; }
    public int StoredFileRowsDeleted { get; set; }
    public int ExportRowsDeleted { get; set; }
    public int LifecycleRowsDeleted { get; set; }
    public int AuditRowsDeleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public class DataRetentionSettingsInfo
{
    public int AnalyticsRetentionDays { get; set; }
    public int AuditRetentionDays { get; set; }
    public int NotificationRetentionDays { get; set; }
    public int DeletedFileRetentionDays { get; set; }
    public int LifecycleHistoryRetentionDays { get; set; }
    public int ExportExpiryDays { get; set; }
    public int WorkspaceDeletionDelayDays { get; set; }
    public bool EnableLegalHolds { get; set; }
}

public class FeatureDefinitionInfo
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
}

public class EffectiveEntitlementInfo
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public string Source { get; set; } = "GlobalFallback";
    public DateTime? ExpiresAt { get; set; }
}

[Tag(ApiTags.Billing)]
[ValidateIsAuthenticated]
[Route("/saas/entitlements", "GET")]
[ValidateHasScope("workspace:read")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class GetEffectiveEntitlements : IGet, IReturn<GetEffectiveEntitlementsResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}
public class GetEffectiveEntitlementsResponse
{
    public List<EffectiveEntitlementInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class StoredFileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long ByteLength { get; set; }
    public string? Sha256 { get; set; }
    public StoredFileStatus Status { get; set; }
    public DateTime CreatedDate { get; set; }
    public string UploadedBy { get; set; } = "";
}

[Tag(ApiTags.Documents)]
[ValidateIsAuthenticated]
[Route("/saas/files", "GET")]
public class QueryStoredFiles : IGet, IReturn<QueryStoredFilesResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public string? Search { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
public class QueryStoredFilesResponse
{
    public List<StoredFileInfo> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Documents)]
[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files", "POST")]
public class UploadStoredFile : IPost, IReturn<StoredFileInfo>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string IdempotencyKey { get; set; } = "";
}

[Tag(ApiTags.Documents)]
[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files/{Id}", "GET")]
public class DownloadStoredFile : IGet, IReturn<byte[]>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Documents)]
[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files/{Id}", "DELETE")]
public class DeleteStoredFile : IDelete, IReturn<EmptyResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

public class UsageSeriesPoint
{
    public DateTime Date { get; set; }
    public long Units { get; set; }
}

public class UsageBreakdownItem
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long Units { get; set; }
}

[Tag(ApiTags.Usage)]
[ValidateIsAuthenticated]
[RequiresFeature("analytics.basic")]
[Route("/saas/usage/analytics", "GET")]
[ValidateHasScope("usage:read")]
public class GetUsageAnalytics : IGet, IReturn<GetUsageAnalyticsResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public string? MeterKey { get; set; }
    public int Days { get; set; } = 30;
}
public class GetUsageAnalyticsResponse
{
    public List<UsageSummary> Usage { get; set; } = [];
    public string MeterKey { get; set; } = "";
    public List<UsageSeriesPoint> Series { get; set; } = [];
    public List<UsageBreakdownItem> ByUser { get; set; } = [];
    public long RejectedOperations { get; set; }
    public long ProjectedPeriodEndUnits { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Usage)]
[ValidateIsAuthenticated]
[RequiresFeature("analytics.basic")]
[Route("/saas/usage/export", "GET")]
[ValidateHasScope("usage:read")]
public class ExportUsageCsv : IGet, IReturn<byte[]>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public string? MeterKey { get; set; }
    public int Days { get; set; } = 30;
}

public class SaasMetricInfo
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public string Format { get; set; } = "number";
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateHasRole("Admin")]
[Route("/saas/admin/analytics", "GET")]
public class GetSaasAnalytics : IGet, IReturn<GetSaasAnalyticsResponse>
{
    public int Days { get; set; } = 30;
}
public class GetSaasAnalyticsResponse
{
    public List<SaasMetricInfo> Metrics { get; set; } = [];
    public List<UsageSeriesPoint> WorkspaceGrowth { get; set; } = [];
    public List<UsageBreakdownItem> PlanMix { get; set; } = [];
    public List<Workspace> QuotaPressure { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class RevenuePoint
{
    public DateTime Date { get; set; }
    public decimal Mrr { get; set; }
    public decimal TrialingMrr { get; set; }
    public long Paying { get; set; }
}

public class PlanRevenueInfo
{
    public string PlanCode { get; set; } = "";
    public string PlanName { get; set; } = "";
    public long Subscriptions { get; set; }
    public decimal Mrr { get; set; }
    [Description("Share of MRR, as a percentage")]
    public decimal Percent { get; set; }
}

[Description("A month's recurring revenue and what moved it")]
public class RevenueMonth
{
    [Description("The first day of the month")]
    public DateTime Month { get; set; }
    public decimal StartMrr { get; set; }
    public decimal EndMrr { get; set; }
    [Description("MRR from customers who started paying")]
    public decimal NewMrr { get; set; }
    [Description("MRR added by customers who moved to a higher price")]
    public decimal ExpansionMrr { get; set; }
    [Description("MRR lost by customers who moved to a lower price")]
    public decimal ContractionMrr { get; set; }
    [Description("MRR lost by customers who stopped paying")]
    public decimal ChurnedMrr { get; set; }
    public int NewCustomers { get; set; }
    public int ChurnedCustomers { get; set; }
    [Description("Change from the start of the month, as a percentage")]
    public decimal? GrowthPercent { get; set; }
}

public class CustomerRevenueInfo
{
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PlanName { get; set; } = "";
    public SubscriptionStatus Status { get; set; }
    public decimal Mrr { get; set; }
}

[Tag(ApiTags.Operations), Tag(ApiTags.Platform)]
[Description("Recurring revenue now, its daily history, each month's new, expansion, contraction and churned MRR, and the largest customers")]
[Notes("MRR is estimated from subscriptions and the plan catalog's prices: Stripe owns discounts, taxes and negotiated prices. " +
       "Its history is recorded each day by the daily snapshot job, and its movement by each subscription.changed audit event.")]
[Tool("the user asks about revenue, MRR, ARR, sales, growth, churn, expansion or which customers bring in the most",
    Safety = ToolSafety.ReadOnly,
    Keywords = ["mrr", "arr", "revenue", "sales", "income", "growth", "churn", "expansion", "monthly business review", "arpa"])]
[ValidateIsAuthenticated]
[Route("/saas/admin/revenue", "GET")]
public class GetRevenueMetrics : IGet, IReturn<GetRevenueMetricsResponse>
{
    [Description("Months of history, including the current month, from 1 to 12")]
    public int Months { get; set; } = 12;
}

public class GetRevenueMetricsResponse
{
    public string Currency { get; set; } = "";
    public decimal Mrr { get; set; }
    public decimal Arr { get; set; }
    [Description("What current trials would add to MRR if they converted")]
    public decimal TrialingMrr { get; set; }
    [Description("Average revenue per paying account")]
    public decimal Arpa { get; set; }
    public long Paying { get; set; }
    public List<PlanRevenueInfo> Plans { get; set; } = [];
    public List<RevenueMonth> Months { get; set; } = [];
    [Description("Recurring revenue for each day of up to 3 months of history, otherwise for the last day of each week")]
    public List<RevenuePoint> Series { get; set; } = [];
    [Description("The ten customers billed the most each month")]
    public List<CustomerRevenueInfo> TopCustomers { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public enum CustomerHealthGrade { Healthy, Watch, AtRisk, Churned }

[Description("Something that raised or lowered a customer's health score")]
public class HealthSignal
{
    public string Key { get; set; } = "";
    [Description("What was found, e.g. API requests are down 62% on the previous 30 days")]
    public string Label { get; set; } = "";
    [Description("How many points it added to or took from the score")]
    public int Impact { get; set; }
}

public class CustomerHealthInfo
{
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public WorkspaceKind Kind { get; set; }
    public string PlanCode { get; set; } = "";
    public string PlanName { get; set; } = "";
    public SubscriptionStatus Status { get; set; }
    public decimal Mrr { get; set; }
    public DateTime CreatedDate { get; set; }
    public int Members { get; set; }
    [Description("API requests in the last 30 days")]
    public long ApiRequests { get; set; }
    [Description("API requests in the 30 days before those")]
    public long PreviousApiRequests { get; set; }
    [Description("Change in API requests on the previous 30 days, as a percentage")]
    public decimal? UsageTrendPercent { get; set; }
    [Description("Share of the current period's API allowance that's used, as a percentage")]
    public decimal? ApiAllowanceUsedPercent { get; set; }
    public DateTime? LastActiveDate { get; set; }
    [Description("Failed payments in the last 90 days")]
    public int PaymentFailures { get; set; }
    [Description("From 0 to 100, higher is healthier")]
    public int Score { get; set; }
    public CustomerHealthGrade Grade { get; set; }
    [Description("Growing or close to a limit, so likely to upgrade")]
    public bool ExpansionCandidate { get; set; }
    public List<HealthSignal> Signals { get; set; } = [];
}

[Tag(ApiTags.Customers), Tag(ApiTags.Platform)]
[Description("Each customer's health score from 0 to 100 and grade, with the signals behind it: usage trend, inactivity, quota pressure, failed payments and billing status")]
[Notes("Customers are ordered least healthy first. Billing amounts are only included for operators who can manage billing.")]
[Tool("the user asks which customers are at risk, healthy, churning, inactive, likely to upgrade or need attention, or how a customer is doing",
    Safety = ToolSafety.ReadOnly,
    Keywords = ["health", "at risk", "churn risk", "attention", "inactive", "upsell", "upgrade", "expansion", "engagement"],
    FollowUps = ["GetSaasCustomer"],
    Examples = ["""{"grade":"AtRisk","take":10}"""])]
[ValidateIsAuthenticated]
[Route("/saas/admin/customer-health", "GET")]
public class GetCustomerHealth : IGet, IReturn<GetCustomerHealthResponse>
{
    [Description("Only this customer")]
    public string? WorkspaceId { get; set; }
    [Description("Only customers with this grade")]
    public CustomerHealthGrade? Grade { get; set; }
    [Description("Include customers whose subscription was canceled")]
    public bool IncludeChurned { get; set; }
    [Description("The most customers to return, from 1 to 200")]
    public int Take { get; set; } = 25;
}

public class GetCustomerHealthResponse
{
    public List<CustomerHealthInfo> Results { get; set; } = [];
    [Description("Customers that matched, before Take")]
    public int Total { get; set; }
    public int Healthy { get; set; }
    public int Watch { get; set; }
    public int AtRisk { get; set; }
    public int Churned { get; set; }
    public string Currency { get; set; } = "";
    [Description("MRR of customers graded AtRisk")]
    public decimal MrrAtRisk { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class MeterDefinitionInfo
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public MeterKind Kind { get; set; }
    public MeterReset Reset { get; set; }
    public MeterAggregation Aggregation { get; set; }
}

public class SaasCustomerDetails
{
    public Workspace Workspace { get; set; } = new();
    public BillingSubscription Subscription { get; set; } = new();
    public PlanInfo Plan { get; set; } = new();
    public List<EffectiveEntitlementInfo> Entitlements { get; set; } = [];
    public List<CustomerEntitlementOverride> Overrides { get; set; } = [];
    public List<UsageSummary> Usage { get; set; } = [];
    public List<WorkspaceMemberInfo> Members { get; set; } = [];
    public List<StoredFileInfo> Files { get; set; } = [];
    public List<SupportNote> SupportNotes { get; set; } = [];
    public List<SaasAuditEvent> AuditEvents { get; set; } = [];
    public List<NotificationDelivery> Notifications { get; set; } = [];
    public List<WorkspaceLifecycleRequest> LifecycleRequests { get; set; } = [];
    public SupportAccessGrant? SupportAccess { get; set; }
    public WorkspaceRetentionPolicy? RetentionPolicy { get; set; }
    public PlatformCapabilitiesInfo Capabilities { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateHasRole("Admin")]
[Route("/saas/admin/customer-overrides/{Id}", "DELETE")]
public class DeleteCustomerOverride : IDelete, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}", "GET")]
public class GetSaasCustomer : IGet, IReturn<SaasCustomerDetails>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/customers", "GET")]
public class QuerySaasCustomers : IGet, IReturn<QuerySaasCustomersResponse>
{
    public string? Search { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
public class QuerySaasCustomersResponse
{
    public List<SaasCustomerSummary> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/operations", "GET")]
public class GetSaasOperations : IGet, IReturn<GetSaasOperationsResponse> { }
public class GetSaasOperationsResponse
{
    public List<FeatureDefinitionInfo> Features { get; set; } = [];
    public List<MeterDefinitionInfo> Meters { get; set; } = [];
    public List<UsageReservation> PendingReservations { get; set; } = [];
    public List<StripeEventInbox> FailedStripeEvents { get; set; } = [];
    public List<NotificationDelivery> FailedNotifications { get; set; } = [];
    public List<WorkspaceLifecycleRequest> ActiveLifecycleRequests { get; set; } = [];
    public List<SupportAccessGrant> ActiveSupportAccess { get; set; } = [];
    public List<DataRetentionRun> RetentionRuns { get; set; } = [];
    public DataRetentionSettingsInfo Retention { get; set; } = new();
    public string ProductName { get; set; } = "";
    public bool StripeConfigured { get; set; }
    public bool EmailEnabled { get; set; }
    public bool SupportAccessEnabled { get; set; }
    public int SupportAccessMaxMinutes { get; set; }
    public PlatformCapabilitiesInfo Capabilities { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/retention", "POST")]
public class UpdateWorkspaceRetentionPolicy : IPost, IReturn<WorkspaceRetentionPolicy>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    public int? AnalyticsRetentionDays { get; set; }
    public int? AuditRetentionDays { get; set; }
    public int? NotificationRetentionDays { get; set; }
    public int? DeletedFileRetentionDays { get; set; }
    public int? LifecycleHistoryRetentionDays { get; set; }
    public bool LegalHold { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/usage/adjust", "POST")]
public class AdjustCustomerGauge : IPost, IReturn<UsageSummary>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string MeterKey { get; set; } = "";
    public long Delta { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string IdempotencyKey { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/billing/reconcile", "POST")]
public class ReconcileSaasCustomerBilling : IPost, IReturn<BillingSubscription>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/stripe-events/{Id}/retry", "POST")]
public class RetryStripeEvent : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/notifications/{Id}/retry", "POST")]
public class RetryNotificationDelivery : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/lifecycle/{Id}/retry", "POST")]
public class RetryWorkspaceLifecycle : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Support)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/support-access", "POST")]
public class CreateSupportAccessGrant : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string OperatorId { get; set; } = "";
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    public int Minutes { get; set; } = 30;
}

[Tag(ApiTags.Support)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/revoke", "POST")]
public class RevokeSupportAccessGrant : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Support)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/start", "POST")]
public class StartSupportAccess : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Support)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/end", "POST")]
public class EndSupportAccess : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Operations)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/operators", "GET")]
public class QueryPlatformOperators : IGet, IReturn<QueryPlatformOperatorsResponse> { }
public class QueryPlatformOperatorsResponse
{
    public List<PlatformOperatorInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/operations/preview", "POST")]
public class PreviewSaasCustomerOperation : IPost, IReturn<PreviewSaasCustomerOperationResponse>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    public PlatformOperationType Operation { get; set; }
    public string? MeterKey { get; set; }
    public long? Delta { get; set; }
    public WorkspaceStatus? Status { get; set; }
}
public class PreviewSaasCustomerOperationResponse
{
    public string Title { get; set; } = "";
    public string Confirmation { get; set; } = "";
    public List<string> Impact { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Notifications)]
[ValidateIsAuthenticated]
[Route("/saas/notifications/preferences", "GET")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class GetNotificationPreferences : IGet, IReturn<GetNotificationPreferencesResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}
public class GetNotificationPreferencesResponse
{
    public List<NotificationPreference> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class NotificationPreferenceInput
{
    public string TemplateKey { get; set; } = "*";
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; }
}

[Tag(ApiTags.Notifications)]
[ValidateIsAuthenticated]
[Route("/saas/notifications/preferences", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class UpdateNotificationPreferences : IPost, IReturn<GetNotificationPreferencesResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public List<NotificationPreferenceInput> Preferences { get; set; } = [];
}

[Tag(ApiTags.Notifications)]
[ValidateIsAuthenticated]
[Route("/saas/notifications", "GET")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class QueryNotifications : IGet, IReturn<QueryNotificationsResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public bool? UnreadOnly { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
public class QueryNotificationsResponse
{
    public List<NotificationDelivery> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Notifications)]
[ValidateIsAuthenticated]
[Route("/saas/notifications/{Id}/read", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class MarkNotificationRead : IPost, IReturn<EmptyResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[Tag(ApiTags.Audit)]
[ValidateIsAuthenticated]
[RequiresFeature("audit.read")]
[Route("/saas/audit", "GET")]
public class QueryWorkspaceAuditEvents : IGet, IReturn<QueryWorkspaceAuditEventsResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public string? Action { get; set; }
    /// <summary>
    /// The Id of the last event of the previous page, to continue after it
    /// </summary>
    public string? AfterId { get; set; }
    public int Take { get; set; } = 100;
}
public class QueryWorkspaceAuditEventsResponse
{
    public List<SaasAuditEvent> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class QueryPlatformAuditEventsResponse
{
    /// <summary>The events of every organization and of the platform, which have no WorkspaceId</summary>
    public List<AuditEventInfo> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Audit)]
[ValidateIsAuthenticated]
[RequiresFeature("audit.read")]
[Route("/saas/audit/export", "GET")]
public class ExportWorkspaceAuditCsv : IGet, IReturn<byte[]>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    public string? Action { get; set; }
    public int Days { get; set; } = 90;
}

[Tag(ApiTags.Audit)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/audit", "GET")]
public class QueryPlatformAuditEvents : IGet, IReturn<QueryPlatformAuditEventsResponse>
{
    public string? Search { get; set; }
    public string? WorkspaceId { get; set; }
    public string? Category { get; set; }
    public string? Action { get; set; }
    public string? Outcome { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}

[Tag(ApiTags.Audit)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/audit/export", "GET")]
public class ExportPlatformAuditCsv : IGet, IReturn<byte[]>
{
    public string? Search { get; set; }
    public string? WorkspaceId { get; set; }
    public string? Category { get; set; }
    public string? Action { get; set; }
    public string? Outcome { get; set; }
    public int Days { get; set; } = 90;
}

[Tag(ApiTags.Support)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/support-notes", "POST")]
public class CreateSupportNote : IPost, IReturn<SupportNote>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Body { get; set; } = "";
}

[Tag(ApiTags.Customers)]
[Tag(ApiTags.Platform)]
[ValidateIsAuthenticated]
[Route("/saas/admin/workspaces/{WorkspaceId}/status", "POST")]
public class ChangeWorkspaceStatus : IPost, IReturn<Workspace>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = default!;
    public WorkspaceStatus Status { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/export", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class CreateWorkspaceExport : IPost, IReturn<WorkspaceLifecycleRequest>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/delete", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class RequestWorkspaceDeletion : IPost, IReturn<WorkspaceLifecycleRequest>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/delete/cancel", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class CancelWorkspaceDeletion : IPost, IReturn<WorkspaceLifecycleRequest>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/transfer", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class TransferWorkspaceOwnership : IPost, IReturn<EmptyResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string TargetUserId { get; set; } = "";
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/leave", "POST")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class LeaveWorkspace : IPost, IReturn<EmptyResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle", "GET")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class GetWorkspaceLifecycle : IGet, IReturn<GetWorkspaceLifecycleResponse>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
}
public class GetWorkspaceLifecycleResponse
{
    public List<WorkspaceLifecycleRequest> Results { get; set; } = [];
    public List<DataExportArtifact> Exports { get; set; } = [];
    public int ExportExpiryDays { get; set; }
    public int WorkspaceDeletionDelayDays { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Tag(ApiTags.Organizations)]
[ValidateIsAuthenticated]
[Route("/saas/lifecycle/exports/{Id}", "GET")]
[WorkspaceAccess(WorkspaceAccess.Account)]
public class DownloadWorkspaceExport : IGet, IReturn<byte[]>, IRequireWorkspace
{
    public string WorkspaceId { get; set; } = default!;
    [ValidateNotEmpty] public string Id { get; set; } = "";
}
