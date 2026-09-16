/* Options:
Date: 2026-09-16 15:21:31
Version: 10.21
Tip: To override a DTO option, remove "//" prefix before updating
BaseUrl: http://127.0.0.1:5005

//GlobalNamespace: 
//MakePropertiesOptional: False
//AddServiceStackTypes: True
//AddResponseStatus: False
//AddImplicitVersion: 
//AddDescriptionAsComments: True
//IncludeTypes: 
//ExcludeTypes: 
//DefaultImports: 
*/

// @ts-nocheck

export interface IReturn<T>
{
    createResponse(): T;
}

export interface IReturnVoid
{
    createResponse(): void;
}

export interface IHasSessionId
{
    sessionId?: string;
}

export interface IHasBearerToken
{
    bearerToken?: string;
}

export interface IGet
{
}

export interface IPost
{
}

export interface IDelete
{
}

export enum NotificationChannel
{
    Email = 'Email',
    InApp = 'InApp',
}

export class NotificationPreferenceInput
{
    public templateKey: string;
    public channel: NotificationChannel;
    public enabled: boolean;

    public constructor(init?: Partial<NotificationPreferenceInput>) { (Object as any).assign(this, init); }
}

export enum WorkspaceStatus
{
    Active = 'Active',
    Suspended = 'Suspended',
    PendingDeletion = 'PendingDeletion',
    Archived = 'Archived',
    Deleted = 'Deleted',
}

export enum PlatformOperationType
{
    GaugeAdjustment = 'GaugeAdjustment',
    BillingReconciliation = 'BillingReconciliation',
    WorkspaceStatusChange = 'WorkspaceStatusChange',
}

export enum WorkspaceMemberRole
{
    Owner = 'Owner',
    Admin = 'Admin',
    Billing = 'Billing',
    Member = 'Member',
}

export enum CouponDuration
{
    Once = 'Once',
    Forever = 'Forever',
    Repeating = 'Repeating',
}

export enum BillingInterval
{
    Month = 'Month',
    Year = 'Year',
}

export class SavePlanPrice
{
    // @Validate(Validator="NotEmpty")
    public currency: string;

    public interval: BillingInterval;
    public unitAmount: number;
    public stripePriceId?: string;
    public isActive: boolean;

    public constructor(init?: Partial<SavePlanPrice>) { (Object as any).assign(this, init); }
}

export class SavePlanFeature
{
    // @Validate(Validator="NotEmpty")
    public key: string;

    // @Validate(Validator="NotEmpty")
    public name: string;

    public description?: string;
    public enabled: boolean;
    public displayOrder: number;

    public constructor(init?: Partial<SavePlanFeature>) { (Object as any).assign(this, init); }
}

export enum QuotaEnforcement
{
    HardLimit = 'HardLimit',
    SoftLimit = 'SoftLimit',
    MeteredOverage = 'MeteredOverage',
}

export class SavePlanQuota
{
    // @Validate(Validator="NotEmpty")
    public meterKey: string;

    // @Validate(Validator="NotEmpty")
    public displayName: string;

    public includedUnits?: number;
    public enforcement: QuotaEnforcement;
    public rolloverEnabled: boolean;

    public constructor(init?: Partial<SavePlanQuota>) { (Object as any).assign(this, init); }
}

// @DataContract
export class QueryBase
{
    // @DataMember(Order=1)
    public skip?: number;

    // @DataMember(Order=2)
    public take?: number;

    // @DataMember(Order=3)
    public orderBy?: string;

    // @DataMember(Order=4)
    public orderByDesc?: string;

    // @DataMember(Order=5)
    public include?: string;

    // @DataMember(Order=6)
    public fields?: string;

    // @DataMember(Order=7)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<QueryBase>) { (Object as any).assign(this, init); }
}

export class QueryDb<T> extends QueryBase
{

    public constructor(init?: Partial<QueryDb<T>>) { super(init); (Object as any).assign(this, init); }
}

export class User
{
    public id: string;
    public userName: string;
    public email?: string;
    public firstName?: string;
    public lastName?: string;
    public displayName?: string;
    public profileUrl?: string;

    public constructor(init?: Partial<User>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseError
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public fieldName: string;

    // @DataMember(Order=3)
    public message: string;

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseError>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseStatus
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public message?: string;

    // @DataMember(Order=3)
    public stackTrace?: string;

    // @DataMember(Order=4)
    public errors?: ResponseError[];

    // @DataMember(Order=5)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseStatus>) { (Object as any).assign(this, init); }
}

export enum StoredFileStatus
{
    Pending = 'Pending',
    Available = 'Available',
    Deleting = 'Deleting',
    Deleted = 'Deleted',
    Failed = 'Failed',
}

export class EffectiveEntitlementInfo
{
    public key: string;
    public displayName: string;
    public enabled: boolean;
    public source: string;
    public expiresAt?: string;

    public constructor(init?: Partial<EffectiveEntitlementInfo>) { (Object as any).assign(this, init); }
}

export class UsageSeriesPoint
{
    public date: string;
    public units: number;

    public constructor(init?: Partial<UsageSeriesPoint>) { (Object as any).assign(this, init); }
}

export class UsageBreakdownItem
{
    public key: string;
    public label: string;
    public units: number;

    public constructor(init?: Partial<UsageBreakdownItem>) { (Object as any).assign(this, init); }
}

export class SaasMetricInfo
{
    public key: string;
    public label: string;
    public value: number;
    public format: string;

    public constructor(init?: Partial<SaasMetricInfo>) { (Object as any).assign(this, init); }
}

export class PlanQuotaInfo
{
    public meterKey: string;
    public displayName: string;
    public includedUnits?: number;
    public enforcement: QuotaEnforcement;
    public rolloverEnabled: boolean;

    public constructor(init?: Partial<PlanQuotaInfo>) { (Object as any).assign(this, init); }
}

export class PlanPriceInfo
{
    public id: string;
    public currency: string;
    public interval: BillingInterval;
    public unitAmount: number;
    public checkoutReady: boolean;

    public constructor(init?: Partial<PlanPriceInfo>) { (Object as any).assign(this, init); }
}

export class PlanInfo
{
    public id: string;
    public code: string;
    public name: string;
    public description: string;
    public isContactSales: boolean;
    public trialDays?: number;
    public features: string[] = [];
    public quotas: PlanQuotaInfo[] = [];
    public prices: PlanPriceInfo[] = [];

    public constructor(init?: Partial<PlanInfo>) { (Object as any).assign(this, init); }
}

export class SaasAuditEvent
{
    public id: string;
    public workspaceId?: string;
    public category: string;
    public action: string;
    public actorId: string;
    public subjectId?: string;
    public detailJson?: string;
    public outcome: string;
    public reason?: string;
    public requestId?: string;
    public ipAddress?: string;
    public userAgent?: string;
    public createdDate: string;

    public constructor(init?: Partial<SaasAuditEvent>) { (Object as any).assign(this, init); }
}

export class SaasAuditBase
{
    public createdDate: string;
    public createdBy: string;
    public modifiedDate: string;
    public modifiedBy: string;

    public constructor(init?: Partial<SaasAuditBase>) { (Object as any).assign(this, init); }
}

export enum NotificationDeliveryStatus
{
    Pending = 'Pending',
    Sending = 'Sending',
    Delivered = 'Delivered',
    Failed = 'Failed',
    Suppressed = 'Suppressed',
}

export class NotificationDelivery extends SaasAuditBase
{
    public id: string;
    public workspaceId?: string;
    public userId: string;
    public recipient: string;
    public templateKey: string;
    public channel: NotificationChannel;
    public subject: string;
    public body: string;
    public deduplicationKey: string;
    public status: NotificationDeliveryStatus;
    public attempts: number;
    public providerId?: string;
    public lastError?: string;
    public deliveredDate?: string;
    public readDate?: string;

    public constructor(init?: Partial<NotificationDelivery>) { super(init); (Object as any).assign(this, init); }
}

export class PlatformCapabilitiesInfo
{
    public canViewCustomers: boolean;
    public canManageBilling: boolean;
    public canManageSupport: boolean;
    public canManagePlatform: boolean;
    public canApproveSupportAccess: boolean;

    public constructor(init?: Partial<PlatformCapabilitiesInfo>) { (Object as any).assign(this, init); }
}

export enum SubscriptionStatus
{
    Free = 'Free',
    Trialing = 'Trialing',
    Active = 'Active',
    PastDue = 'PastDue',
    Paused = 'Paused',
    Canceled = 'Canceled',
}

export class SaasCustomerSummary
{
    public workspaceId: string;
    public name: string;
    public slug: string;
    public status: WorkspaceStatus;
    public billingEmail?: string;
    public planName: string;
    public subscriptionStatus: SubscriptionStatus;
    public stripeCustomerId?: string;
    public stripeSubscriptionId?: string;
    public matchedOn?: string;

    public constructor(init?: Partial<SaasCustomerSummary>) { (Object as any).assign(this, init); }
}

export class FeatureDefinitionInfo
{
    public key: string;
    public displayName: string;
    public description: string;
    public category: string;

    public constructor(init?: Partial<FeatureDefinitionInfo>) { (Object as any).assign(this, init); }
}

export enum MeterKind
{
    Counter = 'Counter',
    Gauge = 'Gauge',
    ReservableCounter = 'ReservableCounter',
}

export enum MeterReset
{
    BillingPeriod = 'BillingPeriod',
    CalendarMonth = 'CalendarMonth',
    Never = 'Never',
}

export enum MeterAggregation
{
    Sum = 'Sum',
    Current = 'Current',
    Maximum = 'Maximum',
}

export class MeterDefinitionInfo
{
    public key: string;
    public displayName: string;
    public unitName: string;
    public kind: MeterKind;
    public reset: MeterReset;
    public aggregation: MeterAggregation;

    public constructor(init?: Partial<MeterDefinitionInfo>) { (Object as any).assign(this, init); }
}

export enum UsageReservationStatus
{
    Pending = 'Pending',
    Settled = 'Settled',
    Released = 'Released',
    Expired = 'Expired',
}

export class UsageReservation extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    // @References("typeof(MyApp.ServiceModel.UsagePeriod)")
    public usagePeriodId: string;

    public meterKey: string;
    public reservedUnits: number;
    public settledUnits?: number;
    public status: UsageReservationStatus;
    public idempotencyKey: string;
    public expiresAt: string;
    public settledDate?: string;
    public metadataJson?: string;

    public constructor(init?: Partial<UsageReservation>) { super(init); (Object as any).assign(this, init); }
}

export enum StripeInboxStatus
{
    Pending = 'Pending',
    Processing = 'Processing',
    Completed = 'Completed',
    Failed = 'Failed',
}

export class StripeEventInbox
{
    public id: string;
    public stripeEventId: string;
    public eventType: string;
    public payloadJson: string;
    public status: StripeInboxStatus;
    public attempts: number;
    public lastError?: string;
    public receivedDate: string;
    public processedDate?: string;

    public constructor(init?: Partial<StripeEventInbox>) { (Object as any).assign(this, init); }
}

export class DataRetentionRun extends SaasAuditBase
{
    public id: number;
    public status: string;
    public usageEventsDeleted: number;
    public usageRollupsDeleted: number;
    public notificationsDeleted: number;
    public storedFileRowsDeleted: number;
    public exportRowsDeleted: number;
    public lifecycleRowsDeleted: number;
    public auditRowsDeleted: number;
    public completedAt?: string;
    public lastError?: string;

    public constructor(init?: Partial<DataRetentionRun>) { super(init); (Object as any).assign(this, init); }
}

export class DataRetentionSettingsInfo
{
    public analyticsRetentionDays: number;
    public auditRetentionDays: number;
    public notificationRetentionDays: number;
    public deletedFileRetentionDays: number;
    public lifecycleHistoryRetentionDays: number;
    public exportExpiryDays: number;
    public workspaceDeletionDelayDays: number;
    public enableLegalHolds: boolean;

    public constructor(init?: Partial<DataRetentionSettingsInfo>) { (Object as any).assign(this, init); }
}

export enum WorkspaceAccessMode
{
    Full = 'Full',
    Grace = 'Grace',
    ReadOnly = 'ReadOnly',
    FreeFallback = 'FreeFallback',
    Suspended = 'Suspended',
}

export class PlatformOperatorInfo
{
    public userId: string;
    public email: string;
    public displayName: string;
    public roles: string[] = [];

    public constructor(init?: Partial<PlatformOperatorInfo>) { (Object as any).assign(this, init); }
}

export class NotificationPreference extends SaasAuditBase
{
    public id: string;
    public userId: string;
    public workspaceId?: string;
    public templateKey: string;
    public channel: NotificationChannel;
    public enabled: boolean;

    public constructor(init?: Partial<NotificationPreference>) { super(init); (Object as any).assign(this, init); }
}

export enum LifecycleRequestType
{
    Export = 'Export',
    Delete = 'Delete',
    CancelDelete = 'CancelDelete',
    TransferOwnership = 'TransferOwnership',
}

export enum LifecycleRequestStatus
{
    Pending = 'Pending',
    Scheduled = 'Scheduled',
    Processing = 'Processing',
    Completed = 'Completed',
    Failed = 'Failed',
    Canceled = 'Canceled',
    Blocked = 'Blocked',
}

export class DataExportArtifact extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.WorkspaceLifecycleRequest)")
    public lifecycleRequestId: string;

    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public objectKey: string;
    public byteLength: number;
    public sha256: string;
    public expiresAt: string;
    public downloadedAt?: string;
    public expiredAt?: string;
    public lastError?: string;

    public constructor(init?: Partial<DataExportArtifact>) { super(init); (Object as any).assign(this, init); }
}

export class WorkspaceApiKeyInfo
{
    public id: number;
    public name: string;
    public visibleKey: string;
    public createdDate: string;
    public expiryDate?: string;
    public lastUsedDate?: string;
    public active: boolean;

    public constructor(init?: Partial<WorkspaceApiKeyInfo>) { (Object as any).assign(this, init); }
}

export enum WorkspaceMemberStatus
{
    Invited = 'Invited',
    Active = 'Active',
    Disabled = 'Disabled',
}

export class SaasPlan extends SaasAuditBase
{
    public id: string;
    public code: string;
    public name: string;
    public description: string;
    public displayOrder: number;
    public isPublic: boolean;
    public isContactSales: boolean;
    public isArchived: boolean;

    public constructor(init?: Partial<SaasPlan>) { super(init); (Object as any).assign(this, init); }
}

export enum PlanVersionStatus
{
    Draft = 'Draft',
    Published = 'Published',
    Retired = 'Retired',
}

export class SaasPlanVersion extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.SaasPlan)")
    public planId: string;

    public version: number;
    public status: PlanVersionStatus;
    public name?: string;
    public description?: string;
    public displayOrder?: number;
    public isPublic?: boolean;
    public isContactSales?: boolean;
    public isArchived?: boolean;
    public trialDays?: number;
    public effectiveFrom?: string;
    public publishedDate?: string;
    public publishedBy?: string;

    public constructor(init?: Partial<SaasPlanVersion>) { super(init); (Object as any).assign(this, init); }
}

export class SaasPlanPrice extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.SaasPlanVersion)")
    public planVersionId: string;

    public currency: string;
    public interval: BillingInterval;
    public unitAmount: number;
    public stripePriceId?: string;
    public isActive: boolean;

    public constructor(init?: Partial<SaasPlanPrice>) { super(init); (Object as any).assign(this, init); }
}

export class SaasPlanFeature extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.SaasPlanVersion)")
    public planVersionId: string;

    public key: string;
    public name: string;
    public description?: string;
    public enabled: boolean;
    public displayOrder: number;

    public constructor(init?: Partial<SaasPlanFeature>) { super(init); (Object as any).assign(this, init); }
}

export class SaasPlanQuota extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.SaasPlanVersion)")
    public planVersionId: string;

    public meterKey: string;
    public displayName: string;
    public includedUnits?: number;
    public enforcement: QuotaEnforcement;
    public rolloverEnabled: boolean;

    public constructor(init?: Partial<SaasPlanQuota>) { super(init); (Object as any).assign(this, init); }
}

export class StripeCatalogPriceMapping
{
    public currency: string;
    public interval: BillingInterval;
    public unitAmount: number;
    public stripePriceId: string;
    public created: boolean;

    public constructor(init?: Partial<StripeCatalogPriceMapping>) { (Object as any).assign(this, init); }
}

// @DataContract
export class QueryResponse<T>
{
    // @DataMember(Order=1)
    public offset: number;

    // @DataMember(Order=2)
    public total: number;

    // @DataMember(Order=3)
    public results: T[] = [];

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    // @DataMember(Order=5)
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryResponse<T>>) { (Object as any).assign(this, init); }
}

export class StoredFileInfo
{
    public id: string;
    public name: string;
    public contentType: string;
    public byteLength: number;
    public sha256?: string;
    public status: StoredFileStatus;
    public createdDate: string;
    public uploadedBy: string;

    public constructor(init?: Partial<StoredFileInfo>) { (Object as any).assign(this, init); }
}

export class QueryStoredFilesResponse
{
    public results: StoredFileInfo[] = [];
    public total: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryStoredFilesResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class EmptyResponse
{
    // @DataMember(Order=1)
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<EmptyResponse>) { (Object as any).assign(this, init); }
}

export class HelloResponse
{
    public result: string;

    public constructor(init?: Partial<HelloResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class RegisterResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public referrerUrl?: string;

    // @DataMember(Order=5)
    public bearerToken?: string;

    // @DataMember(Order=6)
    public refreshToken?: string;

    // @DataMember(Order=7)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=8)
    public roles?: string[];

    // @DataMember(Order=9)
    public permissions?: string[];

    // @DataMember(Order=10)
    public redirectUrl?: string;

    // @DataMember(Order=11)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=12)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<RegisterResponse>) { (Object as any).assign(this, init); }
}

export class GetEffectiveEntitlementsResponse
{
    public results: EffectiveEntitlementInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetEffectiveEntitlementsResponse>) { (Object as any).assign(this, init); }
}

export class UsageSummary
{
    public meterKey: string;
    public displayName: string;
    public usedUnits: number;
    public reservedUnits: number;
    public peakUnits: number;
    public allowance?: number;
    public remainingUnits: number;
    public percentUsed: number;
    public periodStart: string;
    public periodEnd: string;
    public enforcement: QuotaEnforcement;
    public kind: MeterKind;
    public reset: MeterReset;
    public source: string;

    public constructor(init?: Partial<UsageSummary>) { (Object as any).assign(this, init); }
}

export class GetUsageAnalyticsResponse
{
    public usage: UsageSummary[] = [];
    public meterKey: string;
    public series: UsageSeriesPoint[] = [];
    public byUser: UsageBreakdownItem[] = [];
    public rejectedOperations: number;
    public projectedPeriodEndUnits: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetUsageAnalyticsResponse>) { (Object as any).assign(this, init); }
}

export class Workspace extends SaasAuditBase
{
    public id: string;
    public name: string;
    public slug: string;
    public status: WorkspaceStatus;
    public billingEmail?: string;
    public stripeCustomerId?: string;

    public constructor(init?: Partial<Workspace>) { super(init); (Object as any).assign(this, init); }
}

export class GetSaasAnalyticsResponse
{
    public metrics: SaasMetricInfo[] = [];
    public workspaceGrowth: UsageSeriesPoint[] = [];
    public planMix: UsageBreakdownItem[] = [];
    public quotaPressure: Workspace[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasAnalyticsResponse>) { (Object as any).assign(this, init); }
}

export class BillingSubscription extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    // @References("typeof(MyApp.ServiceModel.SaasPlanVersion)")
    public planVersionId: string;

    public status: SubscriptionStatus;
    public stripeSubscriptionId?: string;
    public stripePriceId?: string;
    public interval: BillingInterval;
    public periodStart: string;
    public periodEnd: string;
    public trialEnd?: string;
    public cancelAt?: string;
    public graceEnd?: string;
    public stripeStatus?: string;
    public accessMode: WorkspaceAccessMode;
    public accessReason?: string;

    public constructor(init?: Partial<BillingSubscription>) { super(init); (Object as any).assign(this, init); }
}

export class CustomerEntitlementOverride extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public key: string;
    public enabled?: boolean;
    public quotaUnits?: number;
    public validFrom?: string;
    public validUntil?: string;
    public reason: string;

    public constructor(init?: Partial<CustomerEntitlementOverride>) { super(init); (Object as any).assign(this, init); }
}

export class WorkspaceMemberInfo
{
    public id: string;
    public userId: string;
    public displayName?: string;
    public email?: string;
    public role: WorkspaceMemberRole;
    public status: WorkspaceMemberStatus;
    public invitationEmailSent: boolean;
    public invitationUrl?: string;
    public invitedDate?: string;
    public invitationExpiresAt?: string;
    public invitationExpired: boolean;
    public joinedDate?: string;

    public constructor(init?: Partial<WorkspaceMemberInfo>) { (Object as any).assign(this, init); }
}

export class SupportNote extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public body: string;

    public constructor(init?: Partial<SupportNote>) { super(init); (Object as any).assign(this, init); }
}

export class WorkspaceLifecycleRequest extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public type: LifecycleRequestType;
    public status: LifecycleRequestStatus;
    public requestedBy: string;
    public targetUserId?: string;
    public confirmation?: string;
    public scheduledAt?: string;
    public completedAt?: string;
    public lastError?: string;

    public constructor(init?: Partial<WorkspaceLifecycleRequest>) { super(init); (Object as any).assign(this, init); }
}

export class SupportAccessGrant extends SaasAuditBase
{
    public id: string;
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public operatorId: string;
    public capability: string;
    public reason: string;
    public startsAt: string;
    public expiresAt: string;
    public accessStartedAt?: string;
    public accessEndedAt?: string;
    public revokedAt?: string;

    public constructor(init?: Partial<SupportAccessGrant>) { super(init); (Object as any).assign(this, init); }
}

export class WorkspaceRetentionPolicy extends SaasAuditBase
{
    // @References("typeof(MyApp.ServiceModel.Workspace)")
    public workspaceId: string;

    public analyticsRetentionDays?: number;
    public auditRetentionDays?: number;
    public notificationRetentionDays?: number;
    public deletedFileRetentionDays?: number;
    public lifecycleHistoryRetentionDays?: number;
    public legalHold: boolean;
    public reason?: string;

    public constructor(init?: Partial<WorkspaceRetentionPolicy>) { super(init); (Object as any).assign(this, init); }
}

export class SaasCustomerDetails
{
    public workspace: Workspace;
    public subscription: BillingSubscription;
    public plan: PlanInfo;
    public entitlements: EffectiveEntitlementInfo[] = [];
    public overrides: CustomerEntitlementOverride[] = [];
    public usage: UsageSummary[] = [];
    public members: WorkspaceMemberInfo[] = [];
    public files: StoredFileInfo[] = [];
    public supportNotes: SupportNote[] = [];
    public auditEvents: SaasAuditEvent[] = [];
    public notifications: NotificationDelivery[] = [];
    public lifecycleRequests: WorkspaceLifecycleRequest[] = [];
    public supportAccess?: SupportAccessGrant;
    public retentionPolicy?: WorkspaceRetentionPolicy;
    public capabilities: PlatformCapabilitiesInfo;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<SaasCustomerDetails>) { (Object as any).assign(this, init); }
}

export class QuerySaasCustomersResponse
{
    public results: SaasCustomerSummary[] = [];
    public total: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QuerySaasCustomersResponse>) { (Object as any).assign(this, init); }
}

export class GetSaasOperationsResponse
{
    public features: FeatureDefinitionInfo[] = [];
    public meters: MeterDefinitionInfo[] = [];
    public pendingReservations: UsageReservation[] = [];
    public failedStripeEvents: StripeEventInbox[] = [];
    public failedNotifications: NotificationDelivery[] = [];
    public activeLifecycleRequests: WorkspaceLifecycleRequest[] = [];
    public activeSupportAccess: SupportAccessGrant[] = [];
    public retentionRuns: DataRetentionRun[] = [];
    public retention: DataRetentionSettingsInfo;
    public productName: string;
    public stripeConfigured: boolean;
    public emailEnabled: boolean;
    public supportAccessEnabled: boolean;
    public supportAccessMaxMinutes: number;
    public capabilities: PlatformCapabilitiesInfo;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasOperationsResponse>) { (Object as any).assign(this, init); }
}

export class QueryPlatformOperatorsResponse
{
    public results: PlatformOperatorInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryPlatformOperatorsResponse>) { (Object as any).assign(this, init); }
}

export class GetNotificationPreferencesResponse
{
    public results: NotificationPreference[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetNotificationPreferencesResponse>) { (Object as any).assign(this, init); }
}

export class QueryNotificationsResponse
{
    public results: NotificationDelivery[] = [];
    public total: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryNotificationsResponse>) { (Object as any).assign(this, init); }
}

export class QueryWorkspaceAuditEventsResponse
{
    public results: SaasAuditEvent[] = [];
    public total: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryWorkspaceAuditEventsResponse>) { (Object as any).assign(this, init); }
}

export class PreviewSaasCustomerOperationResponse
{
    public title: string;
    public confirmation: string;
    public impact: string[] = [];
    public warnings: string[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<PreviewSaasCustomerOperationResponse>) { (Object as any).assign(this, init); }
}

export class GetWorkspaceLifecycleResponse
{
    public results: WorkspaceLifecycleRequest[] = [];
    public exports: DataExportArtifact[] = [];
    public exportExpiryDays: number;
    public workspaceDeletionDelayDays: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetWorkspaceLifecycleResponse>) { (Object as any).assign(this, init); }
}

export class GetSaasPlansResponse
{
    public results: PlanInfo[] = [];
    public defaultCurrency: string;
    public annualBillingEnabled: boolean;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasPlansResponse>) { (Object as any).assign(this, init); }
}

export class GetSaasDashboardResponse
{
    public workspace?: Workspace;
    public memberRole: string;
    public plan?: PlanInfo;
    public subscription?: BillingSubscription;
    public usage: UsageSummary[] = [];
    public entitlements: EffectiveEntitlementInfo[] = [];
    public unreadNotifications: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasDashboardResponse>) { (Object as any).assign(this, init); }
}

export class GetWorkspaceApiKeysResponse
{
    public results: WorkspaceApiKeyInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetWorkspaceApiKeysResponse>) { (Object as any).assign(this, init); }
}

export class WorkspaceAccessInfo
{
    public workspace: Workspace;
    public role: WorkspaceMemberRole;
    public isActive: boolean;

    public constructor(init?: Partial<WorkspaceAccessInfo>) { (Object as any).assign(this, init); }
}

export class GetMyWorkspacesResponse
{
    public results: WorkspaceAccessInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetMyWorkspacesResponse>) { (Object as any).assign(this, init); }
}

export class RecordUsageResponse
{
    public accepted: boolean;
    public duplicate: boolean;
    public usage?: UsageSummary;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RecordUsageResponse>) { (Object as any).assign(this, init); }
}

export class GetWorkspaceMembersResponse
{
    public results: WorkspaceMemberInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetWorkspaceMembersResponse>) { (Object as any).assign(this, init); }
}

export class CreateBillingSessionResponse
{
    public url: string;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<CreateBillingSessionResponse>) { (Object as any).assign(this, init); }
}

export class ConfirmCheckoutSessionResponse
{
    public confirmed: boolean;
    public subscriptionStatus: string;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<ConfirmCheckoutSessionResponse>) { (Object as any).assign(this, init); }
}

export class GetSaasAdminResponse
{
    public trialsEnabled: boolean;
    public trialRequiresPaymentMethod: boolean;
    public defaultTrialDays: number;
    public stripeConfigured: boolean;
    public stripeCatalogProvisioningEnabled: boolean;
    public stripeMode: string;
    public plans: SaasPlan[] = [];
    public versions: SaasPlanVersion[] = [];
    public prices: SaasPlanPrice[] = [];
    public features: SaasPlanFeature[] = [];
    public quotas: SaasPlanQuota[] = [];
    public workspaces: Workspace[] = [];
    public recentStripeEvents: StripeEventInbox[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasAdminResponse>) { (Object as any).assign(this, init); }
}

export class SaasPlanDetails
{
    public plan: SaasPlan;
    public version: SaasPlanVersion;
    public hasDraft: boolean;
    public activeSubscriptions: number;
    public prices: SaasPlanPrice[] = [];
    public features: SaasPlanFeature[] = [];
    public quotas: SaasPlanQuota[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<SaasPlanDetails>) { (Object as any).assign(this, init); }
}

export class SaasCouponInfo
{
    public promotionCodeId: string;
    public couponId: string;
    public code: string;
    public name: string;
    public percentOff?: number;
    public amountOff?: number;
    public currency?: string;
    public duration: CouponDuration;
    public durationInMonths?: number;
    public active: boolean;
    public valid: boolean;
    public maxRedemptions?: number;
    public timesRedeemed: number;
    public expiresAt?: string;
    public firstTimeTransaction: boolean;
    public createdDate: string;
    public livemode: boolean;

    public constructor(init?: Partial<SaasCouponInfo>) { (Object as any).assign(this, init); }
}

export class GetSaasCouponsResponse
{
    public stripeConfigured: boolean;
    public results: SaasCouponInfo[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSaasCouponsResponse>) { (Object as any).assign(this, init); }
}

export class ProvisionSaasPlanStripeCatalogResponse
{
    public stripeProductId: string;
    public livemode: boolean;
    public productCreated: boolean;
    public prices: StripeCatalogPriceMapping[] = [];
    public draft: SaasPlanDetails;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<ProvisionSaasPlanStripeCatalogResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class AuthenticateResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public referrerUrl?: string;

    // @DataMember(Order=6)
    public bearerToken?: string;

    // @DataMember(Order=7)
    public refreshToken?: string;

    // @DataMember(Order=8)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=9)
    public profileUrl?: string;

    // @DataMember(Order=10)
    public roles?: string[];

    // @DataMember(Order=11)
    public permissions?: string[];

    // @DataMember(Order=12)
    public authProvider?: string;

    // @DataMember(Order=13)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=14)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<AuthenticateResponse>) { (Object as any).assign(this, init); }
}

// @Route("/saas/files", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QueryStoredFiles implements IReturn<QueryStoredFilesResponse>, IGet
{
    public search?: string;
    public skip: number;
    public take: number;

    public constructor(init?: Partial<QueryStoredFiles>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryStoredFiles'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryStoredFilesResponse(); }
}

// @Route("/saas/files", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class UploadStoredFile implements IReturn<StoredFileInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public idempotencyKey: string;

    public constructor(init?: Partial<UploadStoredFile>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UploadStoredFile'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new StoredFileInfo(); }
}

// @Route("/saas/files/{Id}", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class DownloadStoredFile implements IReturn<Blob>, IGet
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<DownloadStoredFile>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'DownloadStoredFile'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new Blob(); }
}

// @Route("/saas/files/{Id}", "DELETE")
// @ValidateRequest(Validator="IsAuthenticated")
export class DeleteStoredFile implements IReturn<EmptyResponse>, IDelete
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<DeleteStoredFile>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'DeleteStoredFile'; }
    public getMethod() { return 'DELETE'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/lifecycle/exports/{Id}", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class DownloadWorkspaceExport implements IReturn<Blob>, IGet
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<DownloadWorkspaceExport>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'DownloadWorkspaceExport'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new Blob(); }
}

// @Route("/hello/{Name}")
export class Hello implements IReturn<HelloResponse>, IGet
{
    public name: string;

    public constructor(init?: Partial<Hello>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Hello'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new HelloResponse(); }
}

/** @description Sign Up */
// @Api(Description="Sign Up")
// @DataContract
export class Register implements IReturn<RegisterResponse>, IPost
{
    // @DataMember(Order=1)
    public userName?: string;

    // @DataMember(Order=2)
    public firstName?: string;

    // @DataMember(Order=3)
    public lastName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public email?: string;

    // @DataMember(Order=6)
    public password?: string;

    // @DataMember(Order=7)
    public confirmPassword?: string;

    // @DataMember(Order=8)
    public autoLogin?: boolean;

    // @DataMember(Order=10)
    public errorView?: string;

    // @DataMember(Order=11)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Register>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Register'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RegisterResponse(); }
}

// @Route("/confirm-email")
export class ConfirmEmail implements IReturnVoid, IGet
{
    public userId: string;
    public code: string;
    public returnUrl?: string;

    public constructor(init?: Partial<ConfirmEmail>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ConfirmEmail'; }
    public getMethod() { return 'GET'; }
    public createResponse() {}
}

// @Route("/saas/entitlements", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetEffectiveEntitlements implements IReturn<GetEffectiveEntitlementsResponse>, IGet
{

    public constructor(init?: Partial<GetEffectiveEntitlements>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetEffectiveEntitlements'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetEffectiveEntitlementsResponse(); }
}

// @Route("/saas/usage/analytics", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetUsageAnalytics implements IReturn<GetUsageAnalyticsResponse>, IGet
{
    public meterKey?: string;
    public days: number;

    public constructor(init?: Partial<GetUsageAnalytics>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetUsageAnalytics'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetUsageAnalyticsResponse(); }
}

// @Route("/saas/usage/export", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class ExportUsageCsv implements IReturn<Blob>, IGet
{
    public meterKey?: string;
    public days: number;

    public constructor(init?: Partial<ExportUsageCsv>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ExportUsageCsv'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new Blob(); }
}

// @Route("/saas/admin/analytics", "GET")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class GetSaasAnalytics implements IReturn<GetSaasAnalyticsResponse>, IGet
{
    public days: number;

    public constructor(init?: Partial<GetSaasAnalytics>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasAnalytics'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasAnalyticsResponse(); }
}

// @Route("/saas/admin/customers/{WorkspaceId}", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetSaasCustomer implements IReturn<SaasCustomerDetails>, IGet
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    public constructor(init?: Partial<GetSaasCustomer>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasCustomer'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new SaasCustomerDetails(); }
}

// @Route("/saas/admin/customers", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QuerySaasCustomers implements IReturn<QuerySaasCustomersResponse>, IGet
{
    public search?: string;
    public skip: number;
    public take: number;

    public constructor(init?: Partial<QuerySaasCustomers>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QuerySaasCustomers'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QuerySaasCustomersResponse(); }
}

// @Route("/saas/admin/operations", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetSaasOperations implements IReturn<GetSaasOperationsResponse>, IGet
{

    public constructor(init?: Partial<GetSaasOperations>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasOperations'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasOperationsResponse(); }
}

// @Route("/saas/admin/customers/{WorkspaceId}/retention", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class UpdateWorkspaceRetentionPolicy implements IReturn<WorkspaceRetentionPolicy>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    public analyticsRetentionDays?: number;
    public auditRetentionDays?: number;
    public notificationRetentionDays?: number;
    public deletedFileRetentionDays?: number;
    public lifecycleHistoryRetentionDays?: number;
    public legalHold: boolean;
    // @Validate(Validator="NotEmpty")
    public reason: string;

    public constructor(init?: Partial<UpdateWorkspaceRetentionPolicy>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UpdateWorkspaceRetentionPolicy'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceRetentionPolicy(); }
}

// @Route("/saas/admin/customers/{WorkspaceId}/usage/adjust", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class AdjustCustomerGauge implements IReturn<UsageSummary>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    // @Validate(Validator="NotEmpty")
    public meterKey: string;

    public delta: number;
    // @Validate(Validator="NotEmpty")
    public reason: string;

    // @Validate(Validator="NotEmpty")
    public idempotencyKey: string;

    // @Validate(Validator="NotEmpty")
    public confirmation: string;

    public constructor(init?: Partial<AdjustCustomerGauge>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'AdjustCustomerGauge'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new UsageSummary(); }
}

// @Route("/saas/admin/customers/{WorkspaceId}/billing/reconcile", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class ReconcileSaasCustomerBilling implements IReturn<BillingSubscription>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    // @Validate(Validator="NotEmpty")
    public reason: string;

    // @Validate(Validator="NotEmpty")
    public confirmation: string;

    public constructor(init?: Partial<ReconcileSaasCustomerBilling>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ReconcileSaasCustomerBilling'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new BillingSubscription(); }
}

// @Route("/saas/admin/stripe-events/{Id}/retry", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RetryStripeEvent implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<RetryStripeEvent>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RetryStripeEvent'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/admin/notifications/{Id}/retry", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RetryNotificationDelivery implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<RetryNotificationDelivery>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RetryNotificationDelivery'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/admin/lifecycle/{Id}/retry", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RetryWorkspaceLifecycle implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<RetryWorkspaceLifecycle>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RetryWorkspaceLifecycle'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/admin/support-access", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateSupportAccessGrant implements IReturn<SupportAccessGrant>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    // @Validate(Validator="NotEmpty")
    public operatorId: string;

    // @Validate(Validator="NotEmpty")
    public reason: string;

    public minutes: number;

    public constructor(init?: Partial<CreateSupportAccessGrant>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateSupportAccessGrant'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SupportAccessGrant(); }
}

// @Route("/saas/admin/support-access/{Id}/revoke", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RevokeSupportAccessGrant implements IReturn<SupportAccessGrant>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<RevokeSupportAccessGrant>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RevokeSupportAccessGrant'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SupportAccessGrant(); }
}

// @Route("/saas/admin/support-access/{Id}/start", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class StartSupportAccess implements IReturn<SupportAccessGrant>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<StartSupportAccess>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'StartSupportAccess'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SupportAccessGrant(); }
}

// @Route("/saas/admin/support-access/{Id}/end", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class EndSupportAccess implements IReturn<SupportAccessGrant>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<EndSupportAccess>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'EndSupportAccess'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SupportAccessGrant(); }
}

// @Route("/saas/admin/operators", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QueryPlatformOperators implements IReturn<QueryPlatformOperatorsResponse>, IGet
{

    public constructor(init?: Partial<QueryPlatformOperators>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryPlatformOperators'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryPlatformOperatorsResponse(); }
}

// @Route("/saas/notifications/preferences", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetNotificationPreferences implements IReturn<GetNotificationPreferencesResponse>, IGet
{

    public constructor(init?: Partial<GetNotificationPreferences>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetNotificationPreferences'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetNotificationPreferencesResponse(); }
}

// @Route("/saas/notifications/preferences", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class UpdateNotificationPreferences implements IReturn<GetNotificationPreferencesResponse>, IPost
{
    public preferences: NotificationPreferenceInput[] = [];

    public constructor(init?: Partial<UpdateNotificationPreferences>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UpdateNotificationPreferences'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new GetNotificationPreferencesResponse(); }
}

// @Route("/saas/notifications", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QueryNotifications implements IReturn<QueryNotificationsResponse>, IGet
{
    public unreadOnly?: boolean;
    public skip: number;
    public take: number;

    public constructor(init?: Partial<QueryNotifications>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryNotifications'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryNotificationsResponse(); }
}

// @Route("/saas/notifications/{Id}/read", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class MarkNotificationRead implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<MarkNotificationRead>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'MarkNotificationRead'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/audit", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QueryWorkspaceAuditEvents implements IReturn<QueryWorkspaceAuditEventsResponse>, IGet
{
    public action?: string;
    public skip: number;
    public take: number;

    public constructor(init?: Partial<QueryWorkspaceAuditEvents>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryWorkspaceAuditEvents'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryWorkspaceAuditEventsResponse(); }
}

// @Route("/saas/audit/export", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class ExportWorkspaceAuditCsv implements IReturn<Blob>, IGet
{
    public action?: string;
    public days: number;

    public constructor(init?: Partial<ExportWorkspaceAuditCsv>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ExportWorkspaceAuditCsv'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new Blob(); }
}

// @Route("/saas/admin/audit", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class QueryPlatformAuditEvents implements IReturn<QueryWorkspaceAuditEventsResponse>, IGet
{
    public search?: string;
    public workspaceId?: string;
    public category?: string;
    public action?: string;
    public outcome?: string;
    public from?: string;
    public to?: string;
    public skip: number;
    public take: number;

    public constructor(init?: Partial<QueryPlatformAuditEvents>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryPlatformAuditEvents'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryWorkspaceAuditEventsResponse(); }
}

// @Route("/saas/admin/audit/export", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class ExportPlatformAuditCsv implements IReturn<Blob>, IGet
{
    public search?: string;
    public workspaceId?: string;
    public category?: string;
    public action?: string;
    public outcome?: string;
    public days: number;

    public constructor(init?: Partial<ExportPlatformAuditCsv>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ExportPlatformAuditCsv'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new Blob(); }
}

// @Route("/saas/admin/support-notes", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateSupportNote implements IReturn<SupportNote>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    // @Validate(Validator="NotEmpty")
    public body: string;

    public constructor(init?: Partial<CreateSupportNote>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateSupportNote'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SupportNote(); }
}

// @Route("/saas/admin/workspaces/{WorkspaceId}/status", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class ChangeWorkspaceStatus implements IReturn<Workspace>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    public status: WorkspaceStatus;
    // @Validate(Validator="NotEmpty")
    public reason: string;

    // @Validate(Validator="NotEmpty")
    public confirmation: string;

    public constructor(init?: Partial<ChangeWorkspaceStatus>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ChangeWorkspaceStatus'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new Workspace(); }
}

// @Route("/saas/admin/operations/preview", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class PreviewSaasCustomerOperation implements IReturn<PreviewSaasCustomerOperationResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    public operation: PlatformOperationType;
    public meterKey?: string;
    public delta?: number;
    public status?: WorkspaceStatus;

    public constructor(init?: Partial<PreviewSaasCustomerOperation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'PreviewSaasCustomerOperation'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new PreviewSaasCustomerOperationResponse(); }
}

// @Route("/saas/lifecycle/export", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateWorkspaceExport implements IReturn<WorkspaceLifecycleRequest>, IPost
{

    public constructor(init?: Partial<CreateWorkspaceExport>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateWorkspaceExport'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceLifecycleRequest(); }
}

// @Route("/saas/lifecycle/delete", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RequestWorkspaceDeletion implements IReturn<WorkspaceLifecycleRequest>, IPost
{
    // @Validate(Validator="NotEmpty")
    public confirmation: string;

    // @Validate(Validator="NotEmpty")
    public currentPassword: string;

    public constructor(init?: Partial<RequestWorkspaceDeletion>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RequestWorkspaceDeletion'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceLifecycleRequest(); }
}

// @Route("/saas/lifecycle/delete/cancel", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CancelWorkspaceDeletion implements IReturn<WorkspaceLifecycleRequest>, IPost
{

    public constructor(init?: Partial<CancelWorkspaceDeletion>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CancelWorkspaceDeletion'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceLifecycleRequest(); }
}

// @Route("/saas/lifecycle/transfer", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class TransferWorkspaceOwnership implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public targetUserId: string;

    public constructor(init?: Partial<TransferWorkspaceOwnership>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'TransferWorkspaceOwnership'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/lifecycle/leave", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class LeaveWorkspace implements IReturn<EmptyResponse>, IPost
{

    public constructor(init?: Partial<LeaveWorkspace>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'LeaveWorkspace'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/lifecycle", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetWorkspaceLifecycle implements IReturn<GetWorkspaceLifecycleResponse>, IGet
{

    public constructor(init?: Partial<GetWorkspaceLifecycle>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetWorkspaceLifecycle'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetWorkspaceLifecycleResponse(); }
}

// @Route("/saas/plans", "GET")
export class GetSaasPlans implements IReturn<GetSaasPlansResponse>, IGet
{

    public constructor(init?: Partial<GetSaasPlans>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasPlans'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasPlansResponse(); }
}

// @Route("/saas/dashboard", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetSaasDashboard implements IReturn<GetSaasDashboardResponse>, IGet
{

    public constructor(init?: Partial<GetSaasDashboard>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasDashboard'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasDashboardResponse(); }
}

// @Route("/saas/api-keys", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetWorkspaceApiKeys implements IReturn<GetWorkspaceApiKeysResponse>, IGet
{

    public constructor(init?: Partial<GetWorkspaceApiKeys>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetWorkspaceApiKeys'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetWorkspaceApiKeysResponse(); }
}

// @Route("/saas/workspaces", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetMyWorkspaces implements IReturn<GetMyWorkspacesResponse>, IGet
{

    public constructor(init?: Partial<GetMyWorkspaces>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetMyWorkspaces'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetMyWorkspacesResponse(); }
}

// @Route("/saas/organizations", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateOrganization implements IReturn<WorkspaceAccessInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    // @Validate(Validator="Length(2,100)")
    public name: string;

    // @Validate(Validator="Email")
    public billingEmail?: string;

    public constructor(init?: Partial<CreateOrganization>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateOrganization'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceAccessInfo(); }
}

// @Route("/saas/workspaces/active", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class SwitchWorkspace implements IReturn<EmptyResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    public constructor(init?: Partial<SwitchWorkspace>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'SwitchWorkspace'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/usage", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class RecordUsage implements IReturn<RecordUsageResponse>, IPost
{
    public meterKey: string;
    // @Validate(Validator="GreaterThan(0)")
    public units: number;

    // @Validate(Validator="NotEmpty")
    public idempotencyKey: string;

    public metadataJson?: string;

    public constructor(init?: Partial<RecordUsage>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RecordUsage'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RecordUsageResponse(); }
}

// @Route("/saas/workspace", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class UpdateWorkspaceProfile implements IReturn<Workspace>, IPost
{
    // @Validate(Validator="NotEmpty")
    public name: string;

    // @Validate(Validator="NotEmpty")
    public slug: string;

    // @Validate(Validator="Email")
    public billingEmail?: string;

    public constructor(init?: Partial<UpdateWorkspaceProfile>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UpdateWorkspaceProfile'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new Workspace(); }
}

// @Route("/saas/members", "GET")
// @ValidateRequest(Validator="IsAuthenticated")
export class GetWorkspaceMembers implements IReturn<GetWorkspaceMembersResponse>, IGet
{

    public constructor(init?: Partial<GetWorkspaceMembers>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetWorkspaceMembers'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetWorkspaceMembersResponse(); }
}

// @Route("/saas/members", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class InviteWorkspaceMember implements IReturn<WorkspaceMemberInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    // @Validate(Validator="Email")
    public email: string;

    public role: WorkspaceMemberRole;

    public constructor(init?: Partial<InviteWorkspaceMember>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'InviteWorkspaceMember'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceMemberInfo(); }
}

// @Route("/saas/workspace/invitations/{Id}/resend", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class ResendWorkspaceInvitation implements IReturn<WorkspaceMemberInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<ResendWorkspaceInvitation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ResendWorkspaceInvitation'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceMemberInfo(); }
}

// @Route("/saas/invitations/accept", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class AcceptWorkspaceInvitation implements IReturn<WorkspaceAccessInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public token: string;

    public constructor(init?: Partial<AcceptWorkspaceInvitation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'AcceptWorkspaceInvitation'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceAccessInfo(); }
}

// @Route("/saas/workspace/members/{Id}/role", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class UpdateWorkspaceMemberRole implements IReturn<WorkspaceMemberInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public role: WorkspaceMemberRole;

    public constructor(init?: Partial<UpdateWorkspaceMemberRole>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UpdateWorkspaceMemberRole'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new WorkspaceMemberInfo(); }
}

// @Route("/saas/workspace/members/{Id}", "DELETE")
// @ValidateRequest(Validator="IsAuthenticated")
export class RemoveWorkspaceMember implements IReturn<EmptyResponse>, IDelete
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<RemoveWorkspaceMember>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RemoveWorkspaceMember'; }
    public getMethod() { return 'DELETE'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/billing/checkout", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateCheckoutSession implements IReturn<CreateBillingSessionResponse>, IPost
{
    public priceId: string;

    public constructor(init?: Partial<CreateCheckoutSession>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateCheckoutSession'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new CreateBillingSessionResponse(); }
}

// @Route("/saas/billing/checkout/confirm", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class ConfirmCheckoutSession implements IReturn<ConfirmCheckoutSessionResponse>, IPost
{
    public sessionId?: string;

    public constructor(init?: Partial<ConfirmCheckoutSession>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ConfirmCheckoutSession'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new ConfirmCheckoutSessionResponse(); }
}

// @Route("/saas/billing/portal", "POST")
// @ValidateRequest(Validator="IsAuthenticated")
export class CreateCustomerPortalSession implements IReturn<CreateBillingSessionResponse>, IPost
{

    public constructor(init?: Partial<CreateCustomerPortalSession>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateCustomerPortalSession'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new CreateBillingSessionResponse(); }
}

// @Route("/stripe/webhook", "POST")
export class StripeWebhook implements IReturn<EmptyResponse>, IPost
{

    public constructor(init?: Partial<StripeWebhook>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'StripeWebhook'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new EmptyResponse(); }
}

// @Route("/saas/admin", "GET")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class GetSaasAdmin implements IReturn<GetSaasAdminResponse>, IGet
{

    public constructor(init?: Partial<GetSaasAdmin>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasAdmin'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasAdminResponse(); }
}

// @Route("/saas/admin/plans/{PlanId}", "GET")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class GetSaasPlanDetails implements IReturn<SaasPlanDetails>, IGet
{
    public planId: string;

    public constructor(init?: Partial<GetSaasPlanDetails>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasPlanDetails'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new SaasPlanDetails(); }
}

// @Route("/saas/admin/coupons", "GET")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class GetSaasCoupons implements IReturn<GetSaasCouponsResponse>, IGet
{

    public constructor(init?: Partial<GetSaasCoupons>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSaasCoupons'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSaasCouponsResponse(); }
}

// @Route("/saas/admin/coupons", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class CreateSaasCoupon implements IReturn<SaasCouponInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public code: string;

    // @Validate(Validator="NotEmpty")
    public name: string;

    public percentOff?: number;
    public amountOff?: number;
    public currency?: string;
    public duration: CouponDuration;
    public durationInMonths?: number;
    public maxRedemptions?: number;
    public expiresAt?: string;
    public firstTimeTransaction: boolean;

    public constructor(init?: Partial<CreateSaasCoupon>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateSaasCoupon'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SaasCouponInfo(); }
}

// @Route("/saas/admin/coupons/{PromotionCodeId}/deactivate", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class DeactivateSaasCoupon implements IReturn<SaasCouponInfo>, IPost
{
    // @Validate(Validator="NotEmpty")
    public promotionCodeId: string;

    public constructor(init?: Partial<DeactivateSaasCoupon>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'DeactivateSaasCoupon'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SaasCouponInfo(); }
}

// @Route("/saas/admin/plans/{PlanId}/stripe-catalog", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class ProvisionSaasPlanStripeCatalog implements IReturn<ProvisionSaasPlanStripeCatalogResponse>, IPost
{
    // @Validate(Validator="NotEmpty")
    public planId: string;

    public constructor(init?: Partial<ProvisionSaasPlanStripeCatalog>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ProvisionSaasPlanStripeCatalog'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new ProvisionSaasPlanStripeCatalogResponse(); }
}

// @Route("/saas/admin/plans/{PlanId}", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class SaveSaasPlanDraft implements IReturn<SaasPlanDetails>, IPost
{
    // @Validate(Validator="NotEmpty")
    public planId: string;

    // @Validate(Validator="NotEmpty")
    public name: string;

    // @Validate(Validator="NotEmpty")
    public description: string;

    public displayOrder: number;
    public isPublic: boolean;
    public isContactSales: boolean;
    public isArchived: boolean;
    public trialDays?: number;
    public prices: SavePlanPrice[] = [];
    public features: SavePlanFeature[] = [];
    public quotas: SavePlanQuota[] = [];

    public constructor(init?: Partial<SaveSaasPlanDraft>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'SaveSaasPlanDraft'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SaasPlanDetails(); }
}

// @Route("/saas/admin/plans/{PlanId}/publish", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class PublishSaasPlanDraft implements IReturn<SaasPlanDetails>, IPost
{
    // @Validate(Validator="NotEmpty")
    public planId: string;

    public constructor(init?: Partial<PublishSaasPlanDraft>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'PublishSaasPlanDraft'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new SaasPlanDetails(); }
}

// @Route("/saas/admin/overrides", "POST")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class SaveCustomerOverride implements IReturn<CustomerEntitlementOverride>, IPost
{
    public id?: string;
    // @Validate(Validator="NotEmpty")
    public workspaceId: string;

    // @Validate(Validator="NotEmpty")
    public key: string;

    public enabled?: boolean;
    public quotaUnits?: number;
    public validUntil?: string;
    // @Validate(Validator="NotEmpty")
    public reason: string;

    public constructor(init?: Partial<SaveCustomerOverride>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'SaveCustomerOverride'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new CustomerEntitlementOverride(); }
}

// @Route("/saas/admin/customer-overrides/{Id}", "DELETE")
// @ValidateRequest(Validator="HasRole(`Admin`)")
export class DeleteCustomerOverride implements IReturn<EmptyResponse>, IDelete
{
    // @Validate(Validator="NotEmpty")
    public id: string;

    public constructor(init?: Partial<DeleteCustomerOverride>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'DeleteCustomerOverride'; }
    public getMethod() { return 'DELETE'; }
    public createResponse() { return new EmptyResponse(); }
}

/** @description Sign In */
// @Route("/auth", "GET,POST")
// @Route("/auth/{provider}", "POST")
// @Api(Description="Sign In")
// @DataContract
export class Authenticate implements IReturn<AuthenticateResponse>, IPost
{
    /** @description AuthProvider, e.g. credentials */
    // @DataMember(Order=1)
    public provider?: string;

    // @DataMember(Order=2)
    public userName?: string;

    // @DataMember(Order=3)
    public password?: string;

    // @DataMember(Order=4)
    public rememberMe?: boolean;

    // @DataMember(Order=5)
    public accessToken?: string;

    // @DataMember(Order=6)
    public accessTokenSecret?: string;

    // @DataMember(Order=7)
    public returnUrl?: string;

    // @DataMember(Order=8)
    public errorView?: string;

    // @DataMember(Order=9)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Authenticate>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Authenticate'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new AuthenticateResponse(); }
}

// @ValidateRequest(Validator="IsAdmin")
export class QueryUsers extends QueryDb<User> implements IReturn<QueryResponse<User>>
{
    public id?: string;

    public constructor(init?: Partial<QueryUsers>) { super(init); (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryUsers'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryResponse<User>(); }
}

