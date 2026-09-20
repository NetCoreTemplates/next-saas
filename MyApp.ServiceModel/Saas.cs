using ServiceStack;
using ServiceStack.DataAnnotations;

namespace MyApp.ServiceModel;

public enum WorkspaceStatus { Active, Suspended, PendingDeletion, Archived, Deleted }
public enum WorkspaceKind { Individual, Business }
public enum PlanAudience { Both, Individual, Business }
public enum WorkspaceMemberRole { Owner, Admin, Billing, Member }
public enum WorkspaceMemberStatus { Invited, Active, Disabled }
public enum PlanVersionStatus { Draft, Published, Retired }
public enum BillingInterval { Month, Year }
public enum SubscriptionStatus { Free, Trialing, Active, PastDue, Paused, Canceled }
public enum QuotaEnforcement { HardLimit, SoftLimit, MeteredOverage }
public enum StripeInboxStatus { Pending, Processing, Completed, Failed }
public enum CouponDuration { Once, Forever, Repeating }

public abstract class SaasAuditBase
{
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime ModifiedDate { get; set; }
    public string ModifiedBy { get; set; } = "system";
}

[UniqueConstraint(nameof(Slug))]
public class Workspace : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;
    public WorkspaceKind Kind { get; set; } = WorkspaceKind.Individual;
    public string? BillingEmail { get; set; }
    [Index] public string? StripeCustomerId { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(UserId))]
public class WorkspaceMember : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
    [Index] public string UserId { get; set; } = "";
    public WorkspaceMemberRole Role { get; set; } = WorkspaceMemberRole.Member;
    public WorkspaceMemberStatus Status { get; set; } = WorkspaceMemberStatus.Active;
    public string? InvitedEmail { get; set; }
    public DateTime? InvitedDate { get; set; }
    public DateTime? InvitationSentDate { get; set; }
    [Index] public string? InvitationTokenHash { get; set; }
    public DateTime? InvitationExpiresAt { get; set; }
    public DateTime? InvitationAcceptedDate { get; set; }
    public DateTime? InvitationRevokedDate { get; set; }
    public DateTime? JoinedDate { get; set; }
}

public class UserWorkspacePreference : SaasAuditBase
{
    [PrimaryKey] public string UserId { get; set; } = "";
    [References(typeof(Workspace)), Index] public string ActiveWorkspaceId { get; set; } = "";
}

[UniqueConstraint(nameof(Code))]
public class SaasPlan : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsPublic { get; set; } = true;
    public bool IsContactSales { get; set; }
    public bool IsArchived { get; set; }
    public PlanAudience Audience { get; set; } = PlanAudience.Both;
}

[UniqueConstraint(nameof(PlanId), nameof(Version))]
public class SaasPlanVersion : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(SaasPlan))] public string PlanId { get; set; } = "";
    public int Version { get; set; } = 1;
    public PlanVersionStatus Status { get; set; } = PlanVersionStatus.Draft;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsPublic { get; set; }
    public bool? IsContactSales { get; set; }
    public bool? IsArchived { get; set; }
    public PlanAudience? Audience { get; set; }
    public int? TrialDays { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? PublishedDate { get; set; }
    public string? PublishedBy { get; set; }
}

[UniqueConstraint(nameof(PlanVersionId), nameof(Currency), nameof(Interval))]
public class SaasPlanPrice : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(SaasPlanVersion))] public string PlanVersionId { get; set; } = "";
    public string Currency { get; set; } = "usd";
    public BillingInterval Interval { get; set; } = BillingInterval.Month;
    public long UnitAmount { get; set; }
    public string? StripePriceId { get; set; }
    public bool IsActive { get; set; } = true;
}

[UniqueConstraint(nameof(PlanVersionId), nameof(Key))]
public class SaasPlanFeature : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(SaasPlanVersion))] public string PlanVersionId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
}

[UniqueConstraint(nameof(PlanVersionId), nameof(MeterKey))]
public class SaasPlanQuota : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(SaasPlanVersion))] public string PlanVersionId { get; set; } = "";
    public string MeterKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long? IncludedUnits { get; set; }
    public QuotaEnforcement Enforcement { get; set; } = QuotaEnforcement.HardLimit;
    public bool RolloverEnabled { get; set; }
}

public class BillingSubscription : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Unique, References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
    [References(typeof(SaasPlanVersion))] public string PlanVersionId { get; set; } = "";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Free;
    [Index] public string? StripeSubscriptionId { get; set; }
    public string? StripePriceId { get; set; }
    public BillingInterval Interval { get; set; } = BillingInterval.Month;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? TrialEnd { get; set; }
    public DateTime? CancelAt { get; set; }
    public DateTime? GraceEnd { get; set; }
    public string? StripeStatus { get; set; }
    public WorkspaceAccessMode AccessMode { get; set; } = WorkspaceAccessMode.Full;
    public string? AccessReason { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(MeterKey), nameof(PeriodStart))]
public class UsagePeriod : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
    public string MeterKey { get; set; } = "";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long? Allowance { get; set; }
    public QuotaEnforcement Enforcement { get; set; }
    public string Source { get; set; } = "plan";
    public MeterKind Kind { get; set; } = MeterKind.Counter;
    public MeterReset Reset { get; set; } = MeterReset.BillingPeriod;
}

[UniqueConstraint(nameof(UsagePeriodId))]
public class UsageAggregate : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(UsagePeriod))] public string UsagePeriodId { get; set; } = "";
    public long UsedUnits { get; set; }
    public long ReservedUnits { get; set; }
    public long PeakUnits { get; set; }
    public DateTime? LastEventDate { get; set; }
}

[UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
public class UsageEvent
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
    [References(typeof(UsagePeriod))] public string UsagePeriodId { get; set; } = "";
    [Index] public string MeterKey { get; set; } = "";
    public long Units { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string? Source { get; set; }
    public string EventType { get; set; } = "consume";
    public string? MetadataJson { get; set; }
    public DateTime RecordedDate { get; set; }
    public string RecordedBy { get; set; } = "system";
}

[UniqueConstraint(nameof(WorkspaceId), nameof(Key))]
public class CustomerEntitlementOverride : SaasAuditBase
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [References(typeof(Workspace))] public string WorkspaceId { get; set; } = "";
    public string Key { get; set; } = "";
    public bool? Enabled { get; set; }
    public long? QuotaUnits { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Reason { get; set; } = "";
}

[UniqueConstraint(nameof(StripeEventId))]
public class StripeEventInbox
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StripeEventId { get; set; } = "";
    [Index] public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public StripeInboxStatus Status { get; set; } = StripeInboxStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime ReceivedDate { get; set; }
    public DateTime? ProcessedDate { get; set; }
}

public class SaasAuditEvent
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Index] public string? WorkspaceId { get; set; }
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActorId { get; set; } = "system";
    public string? SubjectId { get; set; }
    public string? DetailJson { get; set; }
    public string Outcome { get; set; } = "Succeeded";
    public string? Reason { get; set; }
    public string? RequestId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class PlanPriceInfo
{
    public string Id { get; set; } = "";
    public string Currency { get; set; } = "usd";
    public BillingInterval Interval { get; set; }
    public long UnitAmount { get; set; }
    public bool CheckoutReady { get; set; }
}

public class PlanQuotaInfo
{
    public string MeterKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long? IncludedUnits { get; set; }
    public QuotaEnforcement Enforcement { get; set; }
    public bool RolloverEnabled { get; set; }
}

public class PlanInfo
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public PlanAudience Audience { get; set; } = PlanAudience.Both;
    public bool IsContactSales { get; set; }
    public int? TrialDays { get; set; }
    public List<string> Features { get; set; } = [];
    public List<PlanQuotaInfo> Quotas { get; set; } = [];
    public List<PlanPriceInfo> Prices { get; set; } = [];
}

[Route("/saas/plans", "GET")]
public class GetSaasPlans : IGet, IReturn<GetSaasPlansResponse> { }
public class GetSaasPlansResponse
{
    public List<PlanInfo> Results { get; set; } = [];
    public string DefaultCurrency { get; set; } = "usd";
    public bool AnnualBillingEnabled { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class UsageSummary
{
    public string MeterKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long UsedUnits { get; set; }
    public long ReservedUnits { get; set; }
    public long PeakUnits { get; set; }
    public long? Allowance { get; set; }
    public long RemainingUnits { get; set; }
    public double PercentUsed { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public QuotaEnforcement Enforcement { get; set; }
    public MeterKind Kind { get; set; }
    public MeterReset Reset { get; set; }
    public string Source { get; set; } = "plan";
}

[ValidateIsAuthenticated]
[Route("/saas/dashboard", "GET")]
public class GetSaasDashboard : IGet, IReturn<GetSaasDashboardResponse> { }
public class GetSaasDashboardResponse
{
    public Workspace? Workspace { get; set; }
    public string MemberRole { get; set; } = "";
    public PlanInfo? Plan { get; set; }
    public BillingSubscription? Subscription { get; set; }
    public List<UsageSummary> Usage { get; set; } = [];
    public List<EffectiveEntitlementInfo> Entitlements { get; set; } = [];
    public long UnreadNotifications { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/api-keys", "GET")]
public class GetWorkspaceApiKeys : IGet, IReturn<GetWorkspaceApiKeysResponse> { }
public class GetWorkspaceApiKeysResponse
{
    public List<WorkspaceApiKeyInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}
public class WorkspaceApiKeyInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string VisibleKey { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? LastUsedDate { get; set; }
    public bool Active { get; set; }
}

[ValidateIsAuthenticated]
[RequiresFeature("api.access")]
[Route("/saas/usage", "POST")]
public class RecordUsage : IPost, IReturn<RecordUsageResponse>
{
    public string MeterKey { get; set; } = "api.requests";
    [ValidateGreaterThan(0)] public long Units { get; set; } = 1;
    [ValidateNotEmpty] public string IdempotencyKey { get; set; } = "";
    public string? MetadataJson { get; set; }
}
public class RecordUsageResponse
{
    public bool Accepted { get; set; }
    public bool Duplicate { get; set; }
    public UsageSummary? Usage { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/workspace", "POST")]
public class UpdateWorkspaceProfile : IPost, IReturn<Workspace>
{
    [ValidateNotEmpty] public string Name { get; set; } = "";
    [ValidateNotEmpty] public string Slug { get; set; } = "";
    [ValidateEmail] public string? BillingEmail { get; set; }
}

public class WorkspaceMemberInfo
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public WorkspaceMemberRole Role { get; set; }
    public WorkspaceMemberStatus Status { get; set; }
    public bool InvitationEmailSent { get; set; }
    public string? InvitationUrl { get; set; }
    public DateTime? InvitedDate { get; set; }
    public DateTime? InvitationExpiresAt { get; set; }
    public bool InvitationExpired { get; set; }
    public DateTime? JoinedDate { get; set; }
}

public class WorkspaceAccessInfo
{
    public Workspace Workspace { get; set; } = new();
    public WorkspaceMemberRole Role { get; set; }
    public bool IsActive { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/workspaces", "GET")]
public class GetMyWorkspaces : IGet, IReturn<GetMyWorkspacesResponse> { }

public class GetMyWorkspacesResponse
{
    public List<WorkspaceAccessInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/organizations", "POST")]
public class CreateOrganization : IPost, IReturn<WorkspaceAccessInfo>
{
    [ValidateNotEmpty, ValidateLength(2, 100)] public string Name { get; set; } = "";
    [ValidateEmail] public string? BillingEmail { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/workspaces/active", "POST")]
public class SwitchWorkspace : IPost, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/members", "GET")]
public class GetWorkspaceMembers : IGet, IReturn<GetWorkspaceMembersResponse> { }
public class GetWorkspaceMembersResponse
{
    public List<WorkspaceMemberInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/members", "POST")]
public class InviteWorkspaceMember : IPost, IReturn<WorkspaceMemberInfo>
{
    [ValidateNotEmpty, ValidateEmail] public string Email { get; set; } = "";
    public WorkspaceMemberRole Role { get; set; } = WorkspaceMemberRole.Member;
}

[ValidateIsAuthenticated]
[Route("/saas/workspace/invitations/{Id}/resend", "POST")]
public class ResendWorkspaceInvitation : IPost, IReturn<WorkspaceMemberInfo>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/invitations/accept", "POST")]
public class AcceptWorkspaceInvitation : IPost, IReturn<WorkspaceAccessInfo>
{
    [ValidateNotEmpty] public string Token { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/workspace/members/{Id}/role", "POST")]
public class UpdateWorkspaceMemberRole : IPost, IReturn<WorkspaceMemberInfo>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
    public WorkspaceMemberRole Role { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/workspace/members/{Id}", "DELETE")]
public class RemoveWorkspaceMember : IDelete, IReturn<EmptyResponse>
{
    [ValidateNotEmpty] public string Id { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/billing/checkout", "POST")]
public class CreateCheckoutSession : IPost, IReturn<CreateBillingSessionResponse>
{
    public string PriceId { get; set; } = "";
}

[ValidateIsAuthenticated]
[Route("/saas/billing/portal", "POST")]
public class CreateCustomerPortalSession : IPost, IReturn<CreateBillingSessionResponse> { }
public class CreateBillingSessionResponse
{
    public string Url { get; set; } = "";
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateIsAuthenticated]
[Route("/saas/billing/checkout/confirm", "POST")]
public class ConfirmCheckoutSession : IPost, IReturn<ConfirmCheckoutSessionResponse>
{
    public string? SessionId { get; set; }
}
public class ConfirmCheckoutSessionResponse
{
    public bool Confirmed { get; set; }
    public string SubscriptionStatus { get; set; } = "";
    public ResponseStatus? ResponseStatus { get; set; }
}

[Route("/stripe/webhook", "POST")]
public class StripeWebhook : IPost, IReturn<EmptyResponse> { }

[ValidateHasRole("Admin")]
[Route("/saas/admin", "GET")]
public class GetSaasAdmin : IGet, IReturn<GetSaasAdminResponse> { }
public class GetSaasAdminResponse
{
    public bool TrialsEnabled { get; set; }
    public bool TrialRequiresPaymentMethod { get; set; }
    public int DefaultTrialDays { get; set; } = 14;
    public bool StripeConfigured { get; set; }
    public bool StripeCatalogProvisioningEnabled { get; set; }
    public string StripeMode { get; set; } = "Not configured";
    public List<SaasPlan> Plans { get; set; } = [];
    public List<SaasPlanVersion> Versions { get; set; } = [];
    public List<SaasPlanPrice> Prices { get; set; } = [];
    public List<SaasPlanFeature> Features { get; set; } = [];
    public List<SaasPlanQuota> Quotas { get; set; } = [];
    public List<Workspace> Workspaces { get; set; } = [];
    public List<StripeEventInbox> RecentStripeEvents { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class SaasCouponInfo
{
    public string PromotionCodeId { get; set; } = "";
    public string CouponId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? PercentOff { get; set; }
    public long? AmountOff { get; set; }
    public string? Currency { get; set; }
    public CouponDuration Duration { get; set; }
    public long? DurationInMonths { get; set; }
    public bool Active { get; set; }
    public bool Valid { get; set; }
    public long? MaxRedemptions { get; set; }
    public long TimesRedeemed { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool FirstTimeTransaction { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool Livemode { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/coupons", "GET")]
public class GetSaasCoupons : IGet, IReturn<GetSaasCouponsResponse> { }
public class GetSaasCouponsResponse
{
    public bool StripeConfigured { get; set; }
    public List<SaasCouponInfo> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/coupons", "POST")]
public class CreateSaasCoupon : IPost, IReturn<SaasCouponInfo>
{
    [ValidateNotEmpty] public string Code { get; set; } = "";
    [ValidateNotEmpty] public string Name { get; set; } = "";
    public decimal? PercentOff { get; set; }
    public long? AmountOff { get; set; }
    public string? Currency { get; set; }
    public CouponDuration Duration { get; set; } = CouponDuration.Once;
    public long? DurationInMonths { get; set; }
    public long? MaxRedemptions { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool FirstTimeTransaction { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/coupons/{PromotionCodeId}/deactivate", "POST")]
public class DeactivateSaasCoupon : IPost, IReturn<SaasCouponInfo>
{
    [ValidateNotEmpty] public string PromotionCodeId { get; set; } = "";
}

public class SaasPlanDetails
{
    public SaasPlan Plan { get; set; } = new();
    public SaasPlanVersion Version { get; set; } = new();
    public bool HasDraft { get; set; }
    public long ActiveSubscriptions { get; set; }
    public List<SaasPlanPrice> Prices { get; set; } = [];
    public List<SaasPlanFeature> Features { get; set; } = [];
    public List<SaasPlanQuota> Quotas { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/plans/{PlanId}", "GET")]
public class GetSaasPlanDetails : IGet, IReturn<SaasPlanDetails>
{
    public string PlanId { get; set; } = "";
}

public class SavePlanPrice
{
    [ValidateNotEmpty] public string Currency { get; set; } = "usd";
    public BillingInterval Interval { get; set; }
    public long UnitAmount { get; set; }
    public string? StripePriceId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StripeCatalogPriceMapping
{
    public string Currency { get; set; } = "usd";
    public BillingInterval Interval { get; set; }
    public long UnitAmount { get; set; }
    public string StripePriceId { get; set; } = "";
    public bool Created { get; set; }
}

public class ProvisionSaasPlanStripeCatalogResponse
{
    public string StripeProductId { get; set; } = "";
    public bool Livemode { get; set; }
    public bool ProductCreated { get; set; }
    public List<StripeCatalogPriceMapping> Prices { get; set; } = [];
    public SaasPlanDetails Draft { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/plans/{PlanId}/stripe-catalog", "POST")]
public class ProvisionSaasPlanStripeCatalog : IPost, IReturn<ProvisionSaasPlanStripeCatalogResponse>
{
    [ValidateNotEmpty] public string PlanId { get; set; } = "";
}

public class SavePlanFeature
{
    [ValidateNotEmpty] public string Key { get; set; } = "";
    [ValidateNotEmpty] public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class SavePlanQuota
{
    [ValidateNotEmpty] public string MeterKey { get; set; } = "";
    [ValidateNotEmpty] public string DisplayName { get; set; } = "";
    public long? IncludedUnits { get; set; }
    public QuotaEnforcement Enforcement { get; set; }
    public bool RolloverEnabled { get; set; }
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/plans/{PlanId}", "POST")]
public class SaveSaasPlanDraft : IPost, IReturn<SaasPlanDetails>
{
    [ValidateNotEmpty] public string PlanId { get; set; } = "";
    [ValidateNotEmpty] public string Name { get; set; } = "";
    [ValidateNotEmpty] public string Description { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsPublic { get; set; }
    public bool IsContactSales { get; set; }
    public bool IsArchived { get; set; }
    public PlanAudience Audience { get; set; } = PlanAudience.Both;
    public int? TrialDays { get; set; }
    public List<SavePlanPrice> Prices { get; set; } = [];
    public List<SavePlanFeature> Features { get; set; } = [];
    public List<SavePlanQuota> Quotas { get; set; } = [];
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/plans/{PlanId}/publish", "POST")]
public class PublishSaasPlanDraft : IPost, IReturn<SaasPlanDetails>
{
    [ValidateNotEmpty] public string PlanId { get; set; } = "";
}

[ValidateHasRole("Admin")]
[Route("/saas/admin/overrides", "POST")]
public class SaveCustomerOverride : IPost, IReturn<CustomerEntitlementOverride>
{
    public string? Id { get; set; }
    [ValidateNotEmpty] public string WorkspaceId { get; set; } = "";
    [ValidateNotEmpty] public string Key { get; set; } = "";
    public bool? Enabled { get; set; }
    public long? QuotaUnits { get; set; }
    public DateTime? ValidUntil { get; set; }
    [ValidateNotEmpty] public string Reason { get; set; } = "";
}
