using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyApp.Migrations;

public class Migration1000 : MigrationBase
{
    // The tables as this migration creates them. They're copies of the App's models, which change in later
    // migrations, so this migration always creates the same tables.

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
    public enum MeterKind { Counter, Gauge, ReservableCounter }
    public enum MeterReset { BillingPeriod, CalendarMonth, Never }
    public enum UsageReservationStatus { Pending, Settled, Released, Expired }
    public enum StoredFileStatus { Pending, Available, Deleting, Deleted, Failed }
    public enum NotificationChannel { Email, InApp }
    public enum NotificationDeliveryStatus { Pending, Sending, Delivered, Failed, Suppressed }
    public enum WorkspaceAccessMode { Full, Grace, ReadOnly, FreeFallback, Suspended }
    public enum LifecycleRequestType { Export, Delete, CancelDelete, TransferOwnership }
    public enum LifecycleRequestStatus { Pending, Scheduled, Processing, Completed, Failed, Canceled, Blocked }

    public abstract class SaasAuditBase
    {
        [IgnoreOnUpdate] public DateTime CreatedDate { get; set; }
        [IgnoreOnUpdate] public string CreatedBy { get; set; } = "system";
        public DateTime ModifiedDate { get; set; }
        public string ModifiedBy { get; set; } = "system";
    }

    public abstract class AuditEventBase
    {
        [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Category { get; set; } = "";
        public string Action { get; set; } = "";
        public string UserId { get; set; } = "system";
        public string? SubjectId { get; set; }
        public string? DetailJson { get; set; }
        public string Outcome { get; set; } = "Succeeded";
        public string? Reason { get; set; }
        public string? RequestId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        [Index] public DateTime CreatedDate { get; set; }
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
        [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
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
        [References(typeof(Workspace)), Index] public string ActiveWorkspaceId { get; set; } = default!;
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
        [Unique, References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
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
        [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
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
        [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = default!;
        [References(typeof(UsagePeriod))] public string UsagePeriodId { get; set; } = "";
        public long UsedUnits { get; set; }
        public long ReservedUnits { get; set; }
        public long PeakUnits { get; set; }
        public DateTime? LastEventDate { get; set; }
    }

    [UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
    [CompositeIndex(nameof(WorkspaceId), nameof(MeterKey), nameof(RecordedDate))]
    public class UsageEvent
    {
        [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
        [References(typeof(UsagePeriod))] public string UsagePeriodId { get; set; } = "";
        public string MeterKey { get; set; } = "";
        public long Units { get; set; }
        public string IdempotencyKey { get; set; } = "";
        public string? Source { get; set; }
        public string EventType { get; set; } = "consume";
        public string? MetadataJson { get; set; }
        [Index] public DateTime RecordedDate { get; set; }
        public string RecordedBy { get; set; } = "system";
    }

    [UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
    [CompositeIndex(nameof(Status), nameof(ExpiresAt))]
    public class UsageReservation : SaasAuditBase
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

    [UniqueConstraint(nameof(WorkspaceId), nameof(Date), nameof(MeterKey), nameof(DimensionType), nameof(DimensionValue))]
    public class UsageDailyRollup : SaasAuditBase
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

    [UniqueConstraint(nameof(WorkspaceId), nameof(Key))]
    public class CustomerEntitlementOverride : SaasAuditBase
    {
        [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
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

    [CompositeIndex(nameof(WorkspaceId), nameof(CreatedDate))]
    public class SaasAuditEvent : AuditEventBase
    {
        [References(typeof(Workspace))] public string WorkspaceId { get; set; } = default!;
    }

    public class PlatformAuditEvent : AuditEventBase;

    [UniqueConstraint(nameof(WorkspaceId), nameof(IdempotencyKey))]
    [CompositeIndex(nameof(WorkspaceId), nameof(Status), nameof(CreatedDate))]
    public class StoredFile : SaasAuditBase
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

    [UniqueConstraint(nameof(WorkspaceId), nameof(UserId), nameof(TemplateKey), nameof(Channel))]
    public class NotificationPreference : SaasAuditBase
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
    public class NotificationDelivery : SaasAuditBase
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

    public class SupportNote : SaasAuditBase
    {
        [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [References(typeof(Workspace)), Index] public string WorkspaceId { get; set; } = default!;
        public string Body { get; set; } = "";
    }

    [CompositeIndex(nameof(WorkspaceId), nameof(OperatorId))]
    public class SupportAccessGrant : SaasAuditBase
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

    [CompositeIndex(nameof(WorkspaceId), nameof(Type), nameof(Status))]
    [CompositeIndex(nameof(Status), nameof(ScheduledAt))]
    public class WorkspaceLifecycleRequest : SaasAuditBase
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

    public class DataExportArtifact : SaasAuditBase
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

    public class WorkspaceRetentionPolicy : SaasAuditBase
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

    public override void Up()
    {
        Db.CreateTable<Workspace>();
        Db.CreateTable<WorkspaceMember>();
        Db.CreateTable<UserWorkspacePreference>();
        Db.CreateTable<SaasPlan>();
        Db.CreateTable<SaasPlanVersion>();
        Db.CreateTable<SaasPlanPrice>();
        Db.CreateTable<SaasPlanFeature>();
        Db.CreateTable<SaasPlanQuota>();
        Db.CreateTable<BillingSubscription>();
        Db.CreateTable<UsagePeriod>();
        Db.CreateTable<UsageAggregate>();
        Db.CreateTable<UsageEvent>();
        Db.CreateTable<UsageReservation>();
        Db.CreateTable<UsageDailyRollup>();
        Db.CreateTable<SaasDailySnapshot>();
        Db.CreateTable<CustomerEntitlementOverride>();
        Db.CreateTable<StripeEventInbox>();
        Db.CreateTable<SaasAuditEvent>();
        Db.CreateTable<PlatformAuditEvent>();
        Db.CreateTable<StoredFile>();
        Db.CreateTable<NotificationPreference>();
        Db.CreateTable<NotificationDelivery>();
        Db.CreateTable<SupportNote>();
        Db.CreateTable<SupportAccessGrant>();
        Db.CreateTable<WorkspaceLifecycleRequest>();
        Db.CreateTable<DataExportArtifact>();
        Db.CreateTable<WorkspaceRetentionPolicy>();
        Db.CreateTable<DataRetentionRun>();

        foreach (var plan in LoadPlans())
        {
            SeedPlan(plan.Code, plan.Name, plan.Description, plan.DisplayOrder, plan.ContactSales, plan.Audience,
            plan.Monthly, plan.Annual, plan.Documents, plan.StorageBytes, plan.ApiRequests, plan.Seats,
            plan.Features.Select(x => (x.Key, x.Name)).ToArray(), plan.TrialDays);
        }
    }

    private void SeedPlan(string code, string name, string description, int order, bool contactSales, PlanAudience audience,
        long monthly, long annual, long? documents, long? storageBytes, long? apiRequests,
        long? seats, (string Key, string Name)[] features, int? trialDays)
    {
        var now = DateTime.UtcNow;
        var planId = $"plan.{code}";
        var versionId = $"plan.{code}.v1";
        Db.Insert(new SaasPlan {
            Id = planId, Code = code, Name = name, Description = description, DisplayOrder = order,
            IsContactSales = contactSales, Audience = audience, CreatedDate = now, ModifiedDate = now,
        });
        Db.Insert(new SaasPlanVersion {
            Id = versionId, PlanId = planId, Version = 1, Status = PlanVersionStatus.Published,
            Name = name, Description = description, DisplayOrder = order, IsPublic = true,
            IsContactSales = contactSales, IsArchived = false, Audience = audience,
            TrialDays = trialDays, EffectiveFrom = now, PublishedDate = now,
            PublishedBy = "migration", CreatedDate = now, ModifiedDate = now,
        });
        if (!contactSales)
        {
            Db.Insert(new SaasPlanPrice {
                Id = $"price.{code}.month", PlanVersionId = versionId, Interval = BillingInterval.Month,
                UnitAmount = monthly, CreatedDate = now, ModifiedDate = now,
            });
            Db.Insert(new SaasPlanPrice {
                Id = $"price.{code}.year", PlanVersionId = versionId, Interval = BillingInterval.Year,
                UnitAmount = annual, CreatedDate = now, ModifiedDate = now,
            });
        }
        for (var i = 0; i < features.Length; i++)
        {
            Db.Insert(new SaasPlanFeature {
                Id = $"feature.{code}.{i + 1}", PlanVersionId = versionId, Key = features[i].Key,
                Name = features[i].Name, DisplayOrder = i, CreatedDate = now, ModifiedDate = now,
            });
        }
        InsertQuota(code, versionId, "documents", "documents.stored", "Documents stored", documents, contactSales, now);
        InsertQuota(code, versionId, "storage", "storage.bytes", "Storage", storageBytes, contactSales, now);
        InsertQuota(code, versionId, "uploads", "documents.uploaded", "Documents uploaded", documents == null ? null : documents * 5, true, now);
        InsertQuota(code, versionId, "api", "api.requests", "API requests", apiRequests, contactSales, now);
        Db.Insert(new SaasPlanQuota {
            Id = $"quota.{code}.seats", PlanVersionId = versionId, MeterKey = "workspace.seats",
            DisplayName = "Team seats", IncludedUnits = seats,
            Enforcement = contactSales ? QuotaEnforcement.SoftLimit : QuotaEnforcement.HardLimit,
            CreatedDate = now, ModifiedDate = now,
        });
    }

    private void InsertQuota(string code, string versionId, string id, string meterKey, string displayName,
        long? allowance, bool contactSales, DateTime now) => Db.Insert(new SaasPlanQuota {
            Id = $"quota.{code}.{id}", PlanVersionId = versionId, MeterKey = meterKey,
            DisplayName = displayName, IncludedUnits = allowance,
            Enforcement = contactSales ? QuotaEnforcement.SoftLimit : QuotaEnforcement.HardLimit,
            CreatedDate = now, ModifiedDate = now,
        });

    private static List<PlanSeed> LoadPlans()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "plans.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The plan seed file was not copied to the application output.", path);
        return JsonSerializer.Deserialize<List<PlanSeed>>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        }) ?? throw new InvalidOperationException("plans.json does not contain a valid plan catalog.");
    }

    private sealed class PlanSeed
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public PlanAudience Audience { get; set; } = PlanAudience.Both;
        public string Description { get; set; } = "";
        public int DisplayOrder { get; set; }
        public bool ContactSales { get; set; }
        public int? TrialDays { get; set; }
        public long Monthly { get; set; }
        public long Annual { get; set; }
        public long? Documents { get; set; }
        public long? StorageBytes { get; set; }
        public long? ApiRequests { get; set; }
        public long? Seats { get; set; }
        public List<PlanFeatureSeed> Features { get; set; } = [];
    }

    private sealed class PlanFeatureSeed
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public override void Down()
    {
        Db.DropTable<DataRetentionRun>();
        Db.DropTable<WorkspaceRetentionPolicy>();
        Db.DropTable<DataExportArtifact>();
        Db.DropTable<WorkspaceLifecycleRequest>();
        Db.DropTable<SupportAccessGrant>();
        Db.DropTable<SupportNote>();
        Db.DropTable<NotificationDelivery>();
        Db.DropTable<NotificationPreference>();
        Db.DropTable<StoredFile>();
        Db.DropTable<PlatformAuditEvent>();
        Db.DropTable<SaasAuditEvent>();
        Db.DropTable<StripeEventInbox>();
        Db.DropTable<CustomerEntitlementOverride>();
        Db.DropTable<SaasDailySnapshot>();
        Db.DropTable<UsageDailyRollup>();
        Db.DropTable<UsageReservation>();
        Db.DropTable<UsageEvent>();
        Db.DropTable<UsageAggregate>();
        Db.DropTable<UsagePeriod>();
        Db.DropTable<BillingSubscription>();
        Db.DropTable<SaasPlanQuota>();
        Db.DropTable<SaasPlanFeature>();
        Db.DropTable<SaasPlanPrice>();
        Db.DropTable<SaasPlanVersion>();
        Db.DropTable<SaasPlan>();
        Db.DropTable<WorkspaceMember>();
        Db.DropTable<UserWorkspacePreference>();
        Db.DropTable<Workspace>();
    }
}
