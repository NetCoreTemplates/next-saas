using ServiceStack;
using ServiceStack.DataAnnotations;

namespace MyApp.ServiceModel;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresFeatureAttribute(string featureKey) : Attribute
{
    public string FeatureKey { get; } = featureKey;
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
public class UsageReservation : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    [References(typeof(UsagePeriod)), Index] public string UsagePeriodId { get; set; } = "";
    [Index] public string MeterKey { get; set; } = "";
    public long ReservedUnits { get; set; }
    public long? SettledUnits { get; set; }
    public UsageReservationStatus Status { get; set; } = UsageReservationStatus.Pending;
    [Index] public string IdempotencyKey { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? SettledDate { get; set; }
    public string? MetadataJson { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
public class StoredFile : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    [Index] public string IdempotencyKey { get; set; } = "";
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
public class UsageDailyRollup : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    [Index] public DateTime Date { get; set; }
    [Index] public string MeterKey { get; set; } = "";
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

[UniqueConstraint(nameof(UserId), nameof(TemplateKey), nameof(Channel))]
public class NotificationPreference : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public string UserId { get; set; } = "";
    public string? WorkspaceId { get; set; }
    public string TemplateKey { get; set; } = "*";
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; } = true;
}

[UniqueConstraint(nameof(DeduplicationKey))]
public class NotificationDelivery : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public string? WorkspaceId { get; set; }
    [Index] public string UserId { get; set; } = "";
    public string Recipient { get; set; } = "";
    [Index] public string TemplateKey { get; set; } = "";
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

public class SupportNote : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    public string Body { get; set; } = "";
}

public class SupportAccessGrant : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
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
    public string WorkspaceId { get; set; } = "";
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

public class WorkspaceLifecycleRequest : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    public LifecycleRequestType Type { get; set; }
    public LifecycleRequestStatus Status { get; set; } = LifecycleRequestStatus.Pending;
    public string RequestedBy { get; set; } = "";
    public string? TargetUserId { get; set; }
    public string? Confirmation { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public class DataExportArtifact : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(WorkspaceLifecycleRequest)), Index] public string LifecycleRequestId { get; set; } = "";
    [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? DownloadedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public string? LastError { get; set; }
}

public class WorkspaceRetentionPolicy : SaasAuditBase
{
    [PrimaryKey, References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
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

[ValidateIsAuthenticated]
[Route("/saas/entitlements", "GET")]
public class GetEffectiveEntitlements : IGet, IReturn<GetEffectiveEntitlementsResponse> { }
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

[ValidateIsAuthenticated]
[Route("/saas/files", "GET")]
public class QueryStoredFiles : IGet, IReturn<QueryStoredFilesResponse>
{
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

[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files", "POST")]
public class UploadStoredFile : IPost, IReturn<StoredFileInfo>
{
    [ValidateNotEmpty] public string IdempotencyKey { get; set; } = "";
}

[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files/{Id}", "GET")]
public class DownloadStoredFile : IGet, IReturn<byte[]>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[RequiresFeature("files.basic")]
[Route("/saas/files/{Id}", "DELETE")]
public class DeleteStoredFile : IDelete, IReturn<EmptyResponse>
{
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

[ValidateIsAuthenticated]
[RequiresFeature("analytics.basic")]
[Route("/saas/usage/analytics", "GET")]
public class GetUsageAnalytics : IGet, IReturn<GetUsageAnalyticsResponse>
{
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

[ValidateIsAuthenticated]
[RequiresFeature("analytics.basic")]
[Route("/saas/usage/export", "GET")]
public class ExportUsageCsv : IGet, IReturn<byte[]>
{
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

[ValidateHasRole("Admin")]
[Route("/saas/admin/customer-overrides/{Id}", "DELETE")]
public class DeleteCustomerOverride : IDelete, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}", "GET")]
public class GetSaasCustomer : IGet, IReturn<SaasCustomerDetails>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
}

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

[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/retention", "POST")]
public class UpdateWorkspaceRetentionPolicy : IPost, IReturn<WorkspaceRetentionPolicy>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    public int? AnalyticsRetentionDays { get; set; }
    public int? AuditRetentionDays { get; set; }
    public int? NotificationRetentionDays { get; set; }
    public int? DeletedFileRetentionDays { get; set; }
    public int? LifecycleHistoryRetentionDays { get; set; }
    public bool LegalHold { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/usage/adjust", "POST")]
public class AdjustCustomerGauge : IPost, IReturn<UsageSummary>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    [ValidateNotEmpty] public string MeterKey { get; set; } = "";
    public long Delta { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string IdempotencyKey { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/customers/{WorkspaceId}/billing/reconcile", "POST")]
public class ReconcileSaasCustomerBilling : IPost, IReturn<BillingSubscription>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/stripe-events/{Id}/retry", "POST")]
public class RetryStripeEvent : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/notifications/{Id}/retry", "POST")]
public class RetryNotificationDelivery : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/lifecycle/{Id}/retry", "POST")]
public class RetryWorkspaceLifecycle : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/support-access", "POST")]
public class CreateSupportAccessGrant : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    [ValidateNotEmpty] public string OperatorId { get; set; } = "";
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    public int Minutes { get; set; } = 30;
}

[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/revoke", "POST")]
public class RevokeSupportAccessGrant : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/start", "POST")]
public class StartSupportAccess : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/support-access/{Id}/end", "POST")]
public class EndSupportAccess : IPost, IReturn<SupportAccessGrant>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/operators", "GET")]
public class QueryPlatformOperators : IGet, IReturn<QueryPlatformOperatorsResponse> { }
public class QueryPlatformOperatorsResponse
{
    public List<PlatformOperatorInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/admin/operations/preview", "POST")]
public class PreviewSaasCustomerOperation : IPost, IReturn<PreviewSaasCustomerOperationResponse>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
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

[ValidateIsAuthenticated]
[Route("/saas/notifications/preferences", "GET")]
public class GetNotificationPreferences : IGet, IReturn<GetNotificationPreferencesResponse> { }
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

[ValidateIsAuthenticated]
[Route("/saas/notifications/preferences", "POST")]
public class UpdateNotificationPreferences : IPost, IReturn<GetNotificationPreferencesResponse>
{
    public List<NotificationPreferenceInput> Preferences { get; set; } = [];
}

[ValidateIsAuthenticated]
[Route("/saas/notifications", "GET")]
public class QueryNotifications : IGet, IReturn<QueryNotificationsResponse>
{
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

[ValidateIsAuthenticated]
[Route("/saas/notifications/{Id}/read", "POST")]
public class MarkNotificationRead : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[RequiresFeature("audit.read")]
[Route("/saas/audit", "GET")]
public class QueryWorkspaceAuditEvents : IGet, IReturn<QueryWorkspaceAuditEventsResponse>
{
    public string? Action { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}
public class QueryWorkspaceAuditEventsResponse
{
    public List<SaasAuditEvent> Results { get; set; } = [];
    public long Total { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[RequiresFeature("audit.read")]
[Route("/saas/audit/export", "GET")]
public class ExportWorkspaceAuditCsv : IGet, IReturn<byte[]>
{
    public string? Action { get; set; }
    public int Days { get; set; } = 90;
}

[ValidateIsAuthenticated]
[Route("/saas/admin/audit", "GET")]
public class QueryPlatformAuditEvents : IGet, IReturn<QueryWorkspaceAuditEventsResponse>
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

[ValidateIsAuthenticated]
[Route("/saas/admin/support-notes", "POST")]
public class CreateSupportNote : IPost, IReturn<SupportNote>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    [ValidateNotEmpty] public string Body { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/admin/workspaces/{WorkspaceId}/status", "POST")]
public class ChangeWorkspaceStatus : IPost, IReturn<Workspace>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    public WorkspaceStatus Status { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/export", "POST")]
public class CreateWorkspaceExport : IPost, IReturn<WorkspaceLifecycleRequest> { }

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/delete", "POST")]
public class RequestWorkspaceDeletion : IPost, IReturn<WorkspaceLifecycleRequest>
{
    [ValidateNotEmpty] public string Confirmation { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/delete/cancel", "POST")]
public class CancelWorkspaceDeletion : IPost, IReturn<WorkspaceLifecycleRequest> { }

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/transfer", "POST")]
public class TransferWorkspaceOwnership : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string TargetUserId { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/leave", "POST")]
public class LeaveWorkspace : IPost, IReturn<EmptyResponse> { }

[ValidateIsAuthenticated]
[Route("/saas/lifecycle", "GET")]
public class GetWorkspaceLifecycle : IGet, IReturn<GetWorkspaceLifecycleResponse> { }
public class GetWorkspaceLifecycleResponse
{
    public List<WorkspaceLifecycleRequest> Results { get; set; } = [];
    public List<DataExportArtifact> Exports { get; set; } = [];
    public int ExportExpiryDays { get; set; }
    public int WorkspaceDeletionDelayDays { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/lifecycle/exports/{Id}", "GET")]
public class DownloadWorkspaceExport : IGet, IReturn<byte[]>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}
