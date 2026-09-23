using System.Data;
using System.Text;
using Microsoft.AspNetCore.Identity;
using MyApp.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;
using ServiceStack.Web;

namespace MyApp.ServiceInterface;

public record WorkspaceContext(Workspace Workspace, WorkspaceMember Member, string UserId)
{
    public bool IsAdmin => Member.Role is WorkspaceMemberRole.Owner or WorkspaceMemberRole.Admin;
    public bool CanManageBilling => Member.Role is WorkspaceMemberRole.Owner or WorkspaceMemberRole.Admin or WorkspaceMemberRole.Billing;
}

public static class WorkspaceAuthorization
{
    public static void RequireAdmin(WorkspaceContext context)
    {
        if (!context.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
    }

    public static void RequireBilling(WorkspaceContext context)
    {
        if (!context.CanManageBilling)
            throw new HttpError(403, "BillingRoleRequired", "Organization Owner, Admin, or Billing role is required.");
    }

    public static void RequireOwner(WorkspaceContext context)
    {
        if (context.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Organization Owner role is required.");
    }
}

public enum PlatformCapability
{
    ViewCustomers,
    ManageBilling,
    ManageSupport,
    ManagePlatform,
    ApproveSupportAccess,
}

public static class PlatformAuthorization
{
    public static PlatformCapabilitiesInfo GetCapabilities(IAuthSession session) => new()
    {
        CanViewCustomers = HasAnyRole(session, "Admin", "Support", "BillingAdmin"),
        CanManageBilling = HasAnyRole(session, "Admin", "BillingAdmin"),
        CanManageSupport = HasAnyRole(session, "Admin", "Support"),
        CanManagePlatform = HasAnyRole(session, "Admin"),
        CanApproveSupportAccess = HasAnyRole(session, "Admin"),
    };

    public static bool HasRole(IAuthSession session, string role) =>
        session.Roles?.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase)) == true;

    public static void Require(IAuthSession session, PlatformCapability capability)
    {
        var capabilities = GetCapabilities(session);
        var allowed = capability switch
        {
            PlatformCapability.ViewCustomers => capabilities.CanViewCustomers,
            PlatformCapability.ManageBilling => capabilities.CanManageBilling,
            PlatformCapability.ManageSupport => capabilities.CanManageSupport,
            PlatformCapability.ManagePlatform => capabilities.CanManagePlatform,
            PlatformCapability.ApproveSupportAccess => capabilities.CanApproveSupportAccess,
            _ => false,
        };
        if (!allowed)
            throw new HttpError(403, "PlatformCapabilityRequired", $"The '{capability}' platform capability is required.");
    }

    private static bool HasAnyRole(IAuthSession session, params string[] roles) =>
        roles.Any(role => HasRole(session, role));
}

public static class SupportAccessPolicy
{
    public static bool IsUsable(SupportAccessGrant grant, string workspaceId, string operatorId, DateTime now, bool requireStarted = true) =>
        grant.WorkspaceId == workspaceId && grant.OperatorId == operatorId && grant.StartsAt <= now && grant.ExpiresAt > now &&
        grant.RevokedAt == null && grant.AccessEndedAt == null && (!requireStarted || grant.AccessStartedAt != null);
}

public interface IWorkspaceContextResolver
{
    WorkspaceContext Resolve(IDbConnection db, IAuthSession session, IRequest? request = null);
}

public class WorkspaceContextResolver(ISaasManager manager) : IWorkspaceContextResolver
{
    public WorkspaceContext Resolve(IDbConnection db, IAuthSession session, IRequest? request = null)
    {
        if (session.UserAuthId.IsNullOrEmpty())
            throw HttpError.Unauthorized("Authentication is required.");

        var suppliedApiKey = GetSuppliedApiKey(request);
        if (!suppliedApiKey.IsNullOrEmpty())
            return ResolveApiKey(db, session, suppliedApiKey!);

        var workspace = manager.EnsurePersonalWorkspace(db, session.UserAuthId!, session.DisplayName, session.Email);
        var member = db.Single<WorkspaceMember>(x => x.WorkspaceId == workspace.Id && x.UserId == session.UserAuthId && x.Status == WorkspaceMemberStatus.Active)
            ?? throw new HttpError(403, "WorkspaceAccessDenied", "You do not have access to this organization.");
        return new WorkspaceContext(workspace, member, session.UserAuthId!);
    }

    public WorkspaceContext ResolveApiKey(IDbConnection db, IAuthSession session, string apiKeyValue)
    {
        var apiKey = db.Single<ApiKeysFeature.ApiKey>(x => x.Key == apiKeyValue && x.UserId == session.UserAuthId)
            ?? throw HttpError.Unauthorized("The API key is not valid for this account.");
        if (apiKey.CancelledDate != null || apiKey.ExpiryDate != null && apiKey.ExpiryDate <= DateTime.UtcNow)
            throw HttpError.Unauthorized("The API key is no longer active.");
        if (apiKey.RefIdStr.IsNullOrEmpty())
            throw new HttpError(403, "OrganizationKeyRequired", "This legacy API key is not assigned to an organization. Create a new API key.");

        var keyedWorkspace = db.Single<Workspace>(x => x.Id == apiKey.RefIdStr && x.Status != WorkspaceStatus.Deleted)
            ?? throw new HttpError(403, "OrganizationKeyInvalid", "The API key's organization is unavailable.");
        var apiMember = db.Single<WorkspaceMember>(x => x.WorkspaceId == keyedWorkspace.Id && x.UserId == session.UserAuthId && x.Status == WorkspaceMemberStatus.Active)
            ?? throw new HttpError(403, "WorkspaceAccessDenied", "The API key owner no longer has access to this organization.");
        return new WorkspaceContext(keyedWorkspace, apiMember, session.UserAuthId!);
    }

    public static bool IsApiKeyRequest(IRequest? request) => !GetSuppliedApiKey(request).IsNullOrEmpty();

    public static string? GetSuppliedApiKey(IRequest? request)
    {
        var key = request?.GetHeader("X-Api-Key");
        if (!key.IsNullOrEmpty()) return key!.Trim();
        var authorization = request?.GetHeader(HttpHeaders.Authorization);
        return authorization?.StartsWith("Bearer ak-", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }
}

public interface IEntitlementResolver
{
    List<EffectiveEntitlementInfo> GetEffective(IDbConnection db, Workspace workspace, BillingSubscription subscription);
    bool HasFeature(IDbConnection db, Workspace workspace, BillingSubscription subscription, string featureKey);
}

public class EntitlementResolver(SaasConfig config) : IEntitlementResolver
{
    public List<EffectiveEntitlementInfo> GetEffective(IDbConnection db, Workspace workspace, BillingSubscription subscription)
    {
        var now = DateTime.UtcNow;
        var planVersionId = subscription.PlanVersionId;
        if (subscription.AccessMode == WorkspaceAccessMode.FreeFallback)
        {
            var freePlan = db.Single<SaasPlan>(x => x.Code == config.DefaultPlan && !x.IsArchived);
            planVersionId = db.Select<SaasPlanVersion>(x => x.PlanId == freePlan.Id && x.Status == PlanVersionStatus.Published)
                .OrderByDescending(x => x.Version).First().Id;
        }
        var planFeatures = db.Select<SaasPlanFeature>(x => x.PlanVersionId == planVersionId)
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var overrides = db.Select<CustomerEntitlementOverride>(x => x.WorkspaceId == workspace.Id)
            .Where(x => x.Enabled != null && (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidUntil == null || x.ValidUntil > now))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.ModifiedDate).First(), StringComparer.OrdinalIgnoreCase);
        // Options binding appends configured list entries to initializer defaults. Treat
        // the final entry as the deployment override and expose each stable key once.
        var definitions = config.Features
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var planFeature in planFeatures.Values.Where(x => !definitions.ContainsKey(x.Key)))
            definitions[planFeature.Key] = new SaasFeatureConfig {
                Key = planFeature.Key, DisplayName = planFeature.Name,
                Description = planFeature.Description ?? "", Category = "Plan",
            };

        return definitions.Values.OrderBy(x => x.Category).ThenBy(x => x.DisplayName).Select(definition => {
            var enabled = definition.DefaultEnabled;
            var source = "GlobalFallback";
            DateTime? expiresAt = null;
            if (planFeatures.TryGetValue(definition.Key, out var planFeature))
            {
                enabled = planFeature.Enabled;
                source = subscription.AccessMode == WorkspaceAccessMode.FreeFallback ? "FreeFallback" : "Plan";
            }
            if (overrides.TryGetValue(definition.Key, out var customerOverride))
            {
                enabled = customerOverride.Enabled ?? enabled;
                source = "Override";
                expiresAt = customerOverride.ValidUntil;
            }
            return new EffectiveEntitlementInfo {
                Key = definition.Key, DisplayName = definition.DisplayName, Enabled = enabled,
                Source = source, ExpiresAt = expiresAt,
            };
        }).ToList();
    }

    public bool HasFeature(IDbConnection db, Workspace workspace, BillingSubscription subscription, string featureKey) =>
        GetEffective(db, workspace, subscription).Any(x => x.Key.Equals(featureKey, StringComparison.OrdinalIgnoreCase) && x.Enabled);
}

public interface IFileStore
{
    Task<FileStoreWriteResult> WriteAsync(string objectKey, Stream source, long maximumBytes, CancellationToken token = default);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken token = default);
    Task DeleteAsync(string objectKey, CancellationToken token = default);
    bool Exists(string objectKey);
}

public record FileStoreWriteResult(long ByteLength, string Sha256);

public class SaasPlatformServices(
    SaasConfig config,
    ProductConfig product,
    NotificationConfig notificationConfig,
    ISaasManager manager,
    IWorkspaceContextResolver workspaceContexts,
    IEntitlementResolver entitlements,
    IStripeBillingGateway stripe,
    IBackgroundJobs jobs,
    UserManager<ApplicationUser> userManager) : Service
{
    public async Task<object> Any(GetEffectiveEntitlements request)
    {
        var context = await GetWorkspaceContextAsync();
        var subscription = GetSubscription(context.Workspace.Id);
        manager.EvaluateAccess(Db, context.Workspace, subscription);
        return new GetEffectiveEntitlementsResponse { Results = entitlements.GetEffective(Db, context.Workspace, subscription) };
    }

    public async Task<object> Any(GetUsageAnalytics request)
    {
        var context = await GetWorkspaceContextAsync();
        var subscription = GetSubscription(context.Workspace.Id);
        var usage = manager.GetUsage(Db, context.Workspace, subscription);
        var selected = request.MeterKey.IsNullOrEmpty() ? usage.FirstOrDefault()?.MeterKey ?? "" : request.MeterKey!;
        if (!usage.Any(x => x.MeterKey == selected))
            throw new HttpError(404, "MeterNotFound", "The selected meter is not available to this organization.");
        var days = Math.Clamp(request.Days, 7, Math.Min(365, config.AnalyticsRetentionDays));
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rollups = Db.Select<UsageDailyRollup>(x => x.WorkspaceId == context.Workspace.Id && x.MeterKey == selected && x.Date >= from);
        var events = rollups.Count == 0
            ? Db.Select<UsageEvent>(x => x.WorkspaceId == context.Workspace.Id && x.MeterKey == selected && x.RecordedDate >= from)
            : [];
        var workspaceRollups = rollups.Where(x => x.DimensionType == "workspace" && x.DimensionValue == "all")
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Units));
        var series = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(date => new UsageSeriesPoint {
            Date = date,
            Units = workspaceRollups.TryGetValue(date, out var units) ? units : events.Where(x => x.RecordedDate.Date == date).Sum(x => x.Units),
        }).ToList();
        var meterConfig = config.Meters.LastOrDefault(x => x.Key.Equals(selected, StringComparison.OrdinalIgnoreCase));
        var byUser = meterConfig?.AllowUserBreakdown == false
            ? []
            : rollups.Where(x => x.DimensionType == "user").GroupBy(x => x.DimensionValue)
                .Select(x => new UsageBreakdownItem { Key = x.Key, Label = x.Key, Units = x.Sum(y => y.Units) })
                .Concat(events.GroupBy(x => x.RecordedBy)
                    .Select(x => new UsageBreakdownItem { Key = x.Key, Label = x.Key, Units = x.Sum(y => y.Units) }))
                .OrderByDescending(x => x.Units).Take(10).ToList();
        var current = usage.First(x => x.MeterKey == selected);
        var elapsedDays = Math.Max(1, (DateTime.UtcNow - current.PeriodStart).TotalDays);
        var totalDays = current.Reset == MeterReset.Never ? elapsedDays : Math.Max(elapsedDays, (current.PeriodEnd - current.PeriodStart).TotalDays);
        var projected = current.Kind == MeterKind.Gauge
            ? current.UsedUnits
            : (long)Math.Ceiling(current.UsedUnits / elapsedDays * totalDays);
        var rejected = Db.Count<SaasAuditEvent>(x => x.WorkspaceId == context.Workspace.Id && x.Category == "usage" && x.Action == "quota.rejected");
        return new GetUsageAnalyticsResponse {
            Usage = usage, MeterKey = selected, Series = series, ByUser = byUser,
            RejectedOperations = rejected, ProjectedPeriodEndUnits = projected,
        };
    }

    public async Task<object> Any(ExportUsageCsv request)
    {
        var context = await GetWorkspaceContextAsync();
        var subscription = GetSubscription(context.Workspace.Id);
        var usage = manager.GetUsage(Db, context.Workspace, subscription);
        var meterKey = request.MeterKey.IsNullOrEmpty() ? usage.FirstOrDefault()?.MeterKey ?? "" : request.MeterKey!;
        if (!usage.Any(x => x.MeterKey == meterKey))
            throw new HttpError(404, "MeterNotFound", "The selected meter is not available to this organization.");

        var days = Math.Clamp(request.Days, 1, Math.Min(365, config.AnalyticsRetentionDays));
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rows = Db.Select<UsageEvent>(x => x.WorkspaceId == context.Workspace.Id && x.MeterKey == meterKey && x.RecordedDate >= from)
            .OrderBy(x => x.RecordedDate);
        var csv = new StringBuilder("recordedUtc,meterKey,units,eventType,source,actor,idempotencyKey\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.RecordedDate.ToUniversalTime().ToString("O")), Csv(row.MeterKey), row.Units,
                Csv(row.EventType), Csv(row.Source), Csv(row.RecordedBy), Csv(row.IdempotencyKey)));
        return CsvResult(csv.ToString(), $"usage-{meterKey}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(GetSaasAnalytics request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        var now = DateTime.UtcNow;
        var days = Math.Clamp(request.Days, 7, 365);
        var from = now.Date.AddDays(-(days - 1));
        var workspaces = Db.Select<Workspace>();
        var subscriptions = Db.Select<BillingSubscription>();
        var pricesByStripeId = Db.Select<SaasPlanPrice>().Where(x => !x.StripePriceId.IsNullOrEmpty())
            .GroupBy(x => x.StripePriceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var estimatedMrr = subscriptions.Where(x => x.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing)
            .Where(x => x.StripePriceId != null && pricesByStripeId.ContainsKey(x.StripePriceId))
            .Sum(x => {
                var price = pricesByStripeId[x.StripePriceId!];
                return price.Interval == BillingInterval.Year ? price.UnitAmount / 12m / 100m : price.UnitAmount / 100m;
            });
        var versions = Db.Select<SaasPlanVersion>().ToDictionary(x => x.Id);
        var plans = Db.Select<SaasPlan>().ToDictionary(x => x.Id);
        var planMix = subscriptions.GroupBy(subscription => {
            if (!versions.TryGetValue(subscription.PlanVersionId, out var version) || !plans.TryGetValue(version.PlanId, out var plan)) return "Unknown";
            return plan.Name;
        }).OrderByDescending(x => x.Count()).Select(x => new UsageBreakdownItem { Key = x.Key, Label = x.Key, Units = x.Count() }).ToList();
        var events = Db.Select<UsageEvent>(x => x.RecordedDate >= from);
        var recentPeriods = Db.Select<UsagePeriod>().Where(x => x.Allowance is > 0).ToDictionary(x => x.Id);
        var pressureIds = Db.Select<UsageAggregate>()
            .Where(x => recentPeriods.TryGetValue(x.UsagePeriodId, out var period) && x.UsedUnits * 100d / period.Allowance!.Value >= 80)
            .Select(x => recentPeriods[x.UsagePeriodId].WorkspaceId).Distinct().Take(25).ToList();
        var growth = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(date => new UsageSeriesPoint {
            Date = date, Units = workspaces.LongCount(x => x.CreatedDate.Date == date),
        }).ToList();
        var response = new GetSaasAnalyticsResponse {
            Metrics = [
                new() { Key = "workspaces.active", Label = "Active organizations", Value = workspaces.Count(x => x.Status == WorkspaceStatus.Active) },
                new() { Key = "subscriptions.paid", Label = "Paid subscriptions", Value = subscriptions.Count(x => x.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing) },
                new() { Key = "subscriptions.trialing", Label = "Active trials", Value = subscriptions.Count(x => x.Status == SubscriptionStatus.Trialing) },
                new() { Key = "subscriptions.past_due", Label = "Past due", Value = subscriptions.Count(x => x.Status == SubscriptionStatus.PastDue) },
                new() { Key = "subscriptions.canceling", Label = "Canceling", Value = subscriptions.Count(x => x.CancelAt != null && x.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing) },
                new() { Key = "revenue.mrr_estimate", Label = $"Estimated MRR ({config.DefaultCurrency.ToUpperInvariant()})", Value = estimatedMrr, Format = $"currency:{config.DefaultCurrency.ToUpperInvariant()}" },
                new() { Key = "usage.events", Label = "Usage events", Value = events.Count },
                new() { Key = "usage.rejections", Label = "Quota rejections", Value = Db.Count<SaasAuditEvent>(x => x.Category == "usage" && x.Action == "quota.rejected" && x.CreatedDate >= from) },
                new() { Key = "storage.files", Label = "Available files", Value = Db.Count<StoredFile>(x => x.Status == StoredFileStatus.Available) },
                new() { Key = "operations.stripe_failed", Label = "Failed Stripe events", Value = Db.Count<StripeEventInbox>(x => x.Status == StripeInboxStatus.Failed) },
            ],
            WorkspaceGrowth = growth, PlanMix = planMix,
            QuotaPressure = workspaces.Where(x => pressureIds.Contains(x.Id))
                .Select(x => RedactWorkspace(x, capabilities.CanManageBilling)).ToList(),
        };
        if (!capabilities.CanManageBilling)
            response.Metrics.RemoveAll(x => x.Key.StartsWith("revenue.") || x.Key.StartsWith("subscriptions."));
        return response;
    }

    public async Task<object> Any(GetSaasCustomer request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        SupportAccessGrant? supportAccess = null;
        if (PlatformAuthorization.HasRole(session, "Support") && !PlatformAuthorization.HasRole(session, "Admin"))
        {
            supportAccess = GetActiveSupportAccess(workspace.Id, session.UserAuthId!, requireStarted: true);
            if (supportAccess == null)
            {
                Db.Insert(new SaasAuditEvent {
                    WorkspaceId = workspace.Id, Category = "support", Action = "access.denied", Outcome = "Rejected",
                    ActorId = session.UserAuthId!, SubjectId = workspace.Id, Reason = "No active, started support-access grant.",
                    RequestId = Request?.GetHeader("X-Request-Id"), IpAddress = Request?.RemoteIp, UserAgent = Request?.UserAgent,
                });
                throw new HttpError(403, "SupportAccessRequired", "Start an approved support-access session before viewing this organization.");
            }
        }
        var subscription = GetSubscription(workspace.Id);
        manager.EvaluateAccess(Db, workspace, subscription);
        var details = new SaasCustomerDetails {
            Workspace = RedactWorkspace(workspace, capabilities.CanManageBilling),
            Subscription = RedactSubscription(subscription, capabilities.CanManageBilling),
            Plan = manager.GetPlanInfo(Db, subscription.PlanVersionId),
            Entitlements = entitlements.GetEffective(Db, workspace, subscription),
            Overrides = Db.Select<CustomerEntitlementOverride>(x => x.WorkspaceId == workspace.Id)
                .OrderBy(x => x.Key).ToList(),
            Usage = manager.GetUsage(Db, workspace, subscription),
            Members = Db.Select<WorkspaceMember>(x => x.WorkspaceId == workspace.Id).Select(x => new WorkspaceMemberInfo {
                Id = x.Id, UserId = x.UserId, Email = x.InvitedEmail, Role = x.Role, Status = x.Status,
                InvitationEmailSent = x.InvitationSentDate != null, JoinedDate = x.JoinedDate,
            }).ToList(),
            Files = Db.Select<StoredFile>(x => x.WorkspaceId == workspace.Id && x.Status != StoredFileStatus.Deleted)
                .OrderByDescending(x => x.CreatedDate).Take(25).Select(x => new StoredFileInfo {
                    Id = x.Id, Name = x.Name, ContentType = x.ContentType, ByteLength = x.ByteLength,
                    Sha256 = x.Sha256, Status = x.Status, CreatedDate = x.CreatedDate, UploadedBy = x.UploadedBy,
                }).ToList(),
            SupportNotes = Db.Select<SupportNote>(x => x.WorkspaceId == workspace.Id).OrderByDescending(x => x.CreatedDate).Take(50).ToList(),
            AuditEvents = Db.Select<SaasAuditEvent>(x => x.WorkspaceId == workspace.Id).OrderByDescending(x => x.CreatedDate).Take(100).ToList(),
            Notifications = Db.Select<NotificationDelivery>(x => x.WorkspaceId == workspace.Id).OrderByDescending(x => x.CreatedDate).Take(50).ToList(),
            LifecycleRequests = Db.Select<WorkspaceLifecycleRequest>(x => x.WorkspaceId == workspace.Id).OrderByDescending(x => x.CreatedDate).Take(25).ToList(),
            SupportAccess = supportAccess,
            RetentionPolicy = capabilities.CanManagePlatform
                ? Db.SingleById<WorkspaceRetentionPolicy>(workspace.Id) : null,
            Capabilities = capabilities,
        };
        details.Notifications.ForEach(x => { x.Recipient = ""; x.Body = ""; x.ProviderId = null; });
        if (!capabilities.CanManageSupport)
        {
            details.SupportNotes = [];
            details.Files = [];
            details.Notifications = [];
            details.LifecycleRequests = [];
            details.Members = [];
            details.AuditEvents = [];
        }
        return details;
    }

    public async Task<object> Any(QuerySaasCustomers request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        var search = request.Search?.Trim();
        var workspaces = Db.Select<Workspace>(x => x.Status != WorkspaceStatus.Deleted);
        var subscriptions = Db.Select<BillingSubscription>();
        var versions = Db.Select<SaasPlanVersion>().ToDictionary(x => x.Id);
        var plans = Db.Select<SaasPlan>().ToDictionary(x => x.Id);
        var members = Db.Select<WorkspaceMember>();
        var users = Db.TableExists<User>() ? Db.Select<User>() : [];
        var userEmails = users.ToDictionary(x => x.Id, x => x.Email ?? x.UserName, StringComparer.OrdinalIgnoreCase);
        var apiKeys = Db.TableExists<ApiKeysFeature.ApiKey>() ? Db.Select<ApiKeysFeature.ApiKey>() : [];

        string? Match(Workspace workspace, BillingSubscription? subscription)
        {
            if (search.IsNullOrEmpty()) return "recent organization";
            var term = search!.ToLowerInvariant();
            if (workspace.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return "organization name";
            if (workspace.Slug.Contains(term, StringComparison.OrdinalIgnoreCase)) return "slug";
            if (workspace.BillingEmail?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return "billing email";
            if (workspace.StripeCustomerId?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return "Stripe customer";
            if (subscription?.StripeSubscriptionId?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return "Stripe subscription";
            var workspaceMembers = members.Where(x => x.WorkspaceId == workspace.Id);
            if (workspaceMembers.Any(x => x.InvitedEmail?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
                                          userEmails.TryGetValue(x.UserId, out var email) && email.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return "member email";
            if (apiKeys.Any(x => x.RefIdStr == workspace.Id && x.VisibleKey?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
                return "API-key fingerprint";
            return null;
        }

        var results = workspaces.Select(workspace => {
            var subscription = subscriptions.FirstOrDefault(x => x.WorkspaceId == workspace.Id);
            var matchedOn = Match(workspace, subscription);
            var planName = subscription != null && versions.TryGetValue(subscription.PlanVersionId, out var version) && plans.TryGetValue(version.PlanId, out var plan)
                ? plan.Name : "Unknown";
            return new SaasCustomerSummary {
                WorkspaceId = workspace.Id, Name = workspace.Name, Slug = workspace.Slug, Status = workspace.Status,
                BillingEmail = capabilities.CanManageBilling ? workspace.BillingEmail : null,
                StripeCustomerId = capabilities.CanManageBilling ? workspace.StripeCustomerId : null,
                StripeSubscriptionId = capabilities.CanManageBilling ? subscription?.StripeSubscriptionId : null,
                SubscriptionStatus = subscription?.Status ?? SubscriptionStatus.Free,
                PlanName = planName, MatchedOn = matchedOn,
            };
        }).Where(x => x.MatchedOn != null).OrderByDescending(x => workspaces.First(y => y.Id == x.WorkspaceId).CreatedDate).ToList();
        return new QuerySaasCustomersResponse {
            Total = results.Count,
            Results = results.Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 100)).ToList(),
        };
    }

    public async Task<object> Any(GetSaasOperations request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var now = DateTime.UtcNow;
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        return new GetSaasOperationsResponse {
            ProductName = product.ProductName,
            StripeConfigured = stripe.IsConfigured,
            EmailEnabled = notificationConfig.Provider == EmailProvider.Smtp,
            SupportAccessEnabled = config.EnableSupportAccess,
            SupportAccessMaxMinutes = config.SupportAccessMaxMinutes,
            Capabilities = capabilities,
            Features = config.Features.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).Select(x => new FeatureDefinitionInfo {
                Key = x.Key, DisplayName = x.DisplayName, Description = x.Description, Category = x.Category,
            }).ToList(),
            Meters = config.Meters.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).Select(x => new MeterDefinitionInfo {
                Key = x.Key, DisplayName = x.DisplayName, UnitName = x.UnitName,
                Kind = x.Kind, Reset = x.Reset, Aggregation = x.Aggregation,
            }).ToList(),
            PendingReservations = capabilities.CanManagePlatform ? Db.Select<UsageReservation>(x => x.Status == UsageReservationStatus.Pending).OrderBy(x => x.ExpiresAt).Take(100).ToList() : [],
            FailedStripeEvents = capabilities.CanManageBilling ? Db.Select<StripeEventInbox>(x => x.Status == StripeInboxStatus.Failed).OrderByDescending(x => x.ReceivedDate).Take(100).ToList() : [],
            FailedNotifications = capabilities.CanManageSupport ? Db.Select<NotificationDelivery>(x => x.Status == NotificationDeliveryStatus.Failed).OrderByDescending(x => x.ModifiedDate).Take(100).ToList() : [],
            ActiveLifecycleRequests = capabilities.CanManagePlatform ? Db.Select<WorkspaceLifecycleRequest>()
                .Where(x => x.Status is LifecycleRequestStatus.Pending or LifecycleRequestStatus.Scheduled or LifecycleRequestStatus.Processing or LifecycleRequestStatus.Failed or LifecycleRequestStatus.Blocked)
                .OrderByDescending(x => x.CreatedDate).Take(100).ToList() : [],
            ActiveSupportAccess = Db.Select<SupportAccessGrant>(x => x.RevokedAt == null && x.AccessEndedAt == null && x.StartsAt <= now && x.ExpiresAt > now)
                .Where(x => capabilities.CanApproveSupportAccess || x.OperatorId == session.UserAuthId)
                .OrderBy(x => x.ExpiresAt).ToList(),
            Retention = new DataRetentionSettingsInfo {
                AnalyticsRetentionDays = config.AnalyticsRetentionDays,
                AuditRetentionDays = config.AuditRetentionDays,
                NotificationRetentionDays = config.NotificationRetentionDays,
                DeletedFileRetentionDays = config.DeletedFileRetentionDays,
                LifecycleHistoryRetentionDays = config.LifecycleHistoryRetentionDays,
                ExportExpiryDays = config.ExportExpiryDays,
                WorkspaceDeletionDelayDays = config.WorkspaceDeletionDelayDays,
                EnableLegalHolds = config.EnableLegalHolds,
            },
            RetentionRuns = capabilities.CanManagePlatform && Db.TableExists<DataRetentionRun>()
                ? Db.Select<DataRetentionRun>().OrderByDescending(x => x.CreatedDate).Take(10).ToList() : [],
        };
    }

    public async Task<object> Any(UpdateWorkspaceRetentionPolicy request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        if (request.LegalHold && !config.EnableLegalHolds)
            throw new HttpError(409, "LegalHoldsDisabled", "Legal holds are disabled by global configuration.");
        foreach (var days in new[] { request.AnalyticsRetentionDays, request.AuditRetentionDays,
                     request.NotificationRetentionDays, request.DeletedFileRetentionDays, request.LifecycleHistoryRetentionDays })
            if (days is < 1 or > 3650)
                throw new HttpError(400, "InvalidRetentionPeriod", "Customer retention periods must be between 1 and 3650 days.");
        var now = DateTime.UtcNow;
        var row = Db.SingleById<WorkspaceRetentionPolicy>(workspace.Id) ?? new WorkspaceRetentionPolicy {
            WorkspaceId = workspace.Id, CreatedDate = now, CreatedBy = session.UserAuthId,
        };
        row.AnalyticsRetentionDays = request.AnalyticsRetentionDays;
        row.AuditRetentionDays = request.AuditRetentionDays;
        row.NotificationRetentionDays = request.NotificationRetentionDays;
        row.DeletedFileRetentionDays = request.DeletedFileRetentionDays;
        row.LifecycleHistoryRetentionDays = request.LifecycleHistoryRetentionDays;
        row.LegalHold = request.LegalHold;
        row.Reason = request.Reason.Trim();
        row.ModifiedDate = now;
        row.ModifiedBy = session.UserAuthId;
        Db.Save(row);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "lifecycle", Action = "retention.policy-updated",
            ActorId = session.UserAuthId!, SubjectId = workspace.Id, Reason = row.Reason,
            DetailJson = new { row.LegalHold, row.AnalyticsRetentionDays, row.AuditRetentionDays,
                row.NotificationRetentionDays, row.DeletedFileRetentionDays, row.LifecycleHistoryRetentionDays }.ToJson(),
            CreatedDate = now,
        });
        return row;
    }

    public async Task<object> Any(AdjustCustomerGauge request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        if (request.Delta == 0)
            throw new HttpError(400, "AdjustmentRequired", "The adjustment must be greater or less than zero.");
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        RequireConfirmation(workspace, request.Confirmation);
        var subscription = GetSubscription(workspace.Id);
        var meter = config.Meters.LastOrDefault(x => x.Key.Equals(request.MeterKey, StringComparison.OrdinalIgnoreCase));
        if (meter == null || meter.Kind != MeterKind.Gauge)
            throw new HttpError(409, "GaugeRequired", "Only gauge meters can be corrected by an operator.");

        var result = manager.AdjustGauge(Db, workspace, subscription, session.UserAuthId!, request.MeterKey,
            request.Delta, request.IdempotencyKey, "operator-correction");
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "usage", Action = "gauge.adjusted", ActorId = session.UserAuthId!,
            SubjectId = request.MeterKey, Reason = request.Reason.Trim(),
            DetailJson = new { request.Delta, request.IdempotencyKey, result.UsedUnits }.ToJson(), CreatedDate = DateTime.UtcNow,
        });
        return result;
    }

    public async Task<object> Any(ReconcileSaasCustomerBilling request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageBilling);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        RequireConfirmation(workspace, request.Confirmation);
        if (!await stripe.ReconcileSubscriptionAsync(Db, workspace))
            throw new HttpError(409, "StripeSubscriptionNotFound", "This organization does not have a Stripe subscription to reconcile.");
        var subscription = GetSubscription(workspace.Id);
        manager.EvaluateAccess(Db, workspace, subscription);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id,
            Category = "billing",
            Action = "subscription.reconciled",
            ActorId = session.UserAuthId!,
            SubjectId = subscription.StripeSubscriptionId,
            Reason = request.Reason.Trim(),
            CreatedDate = DateTime.UtcNow,
        });
        return subscription;
    }

    public async Task<object> Any(RetryStripeEvent request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageBilling);
        var row = Db.SingleById<StripeEventInbox>(request.Id)
            ?? throw new HttpError(404, "StripeEventNotFound", "The Stripe event was not found.");
        if (row.Status == StripeInboxStatus.Completed)
            throw new HttpError(409, "StripeEventCompleted", "Completed Stripe events do not need to be retried.");
        row.Status = StripeInboxStatus.Pending;
        row.LastError = null;
        Db.Update(row);
        Db.Insert(new SaasAuditEvent { Category = "stripe", Action = "event.retry-requested", ActorId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueCommand<ProcessStripeEventCommand>(new ProcessStripeEvent { InboxId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(RetryNotificationDelivery request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var row = Db.SingleById<NotificationDelivery>(request.Id)
            ?? throw new HttpError(404, "NotificationNotFound", "The notification delivery was not found.");
        if (!PlatformAuthorization.HasRole(session, "Admin") &&
            (row.WorkspaceId.IsNullOrEmpty() || GetActiveSupportAccess(row.WorkspaceId!, session.UserAuthId!, true) == null))
            throw new HttpError(403, "SupportAccessRequired", "An active support session is required to retry this organization's notification.");
        row.Status = NotificationDeliveryStatus.Pending;
        row.LastError = null;
        row.ModifiedDate = DateTime.UtcNow;
        row.ModifiedBy = session.UserAuthId!;
        Db.Update(row);
        Db.Insert(new SaasAuditEvent { WorkspaceId = row.WorkspaceId, Category = "notification", Action = "delivery.retry-requested", ActorId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueCommand<ProcessNotificationDeliveryCommand>(new ProcessNotificationDelivery { DeliveryId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(RetryWorkspaceLifecycle request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var row = Db.SingleById<WorkspaceLifecycleRequest>(request.Id)
            ?? throw new HttpError(404, "LifecycleRequestNotFound", "The lifecycle request was not found.");
        if (row.Status != LifecycleRequestStatus.Failed)
            throw new HttpError(409, "LifecycleRequestNotFailed", "Only failed lifecycle requests can be retried.");
        row.Status = LifecycleRequestStatus.Pending;
        row.LastError = null;
        row.ModifiedDate = DateTime.UtcNow;
        row.ModifiedBy = session.UserAuthId!;
        Db.Update(row);
        Db.Insert(new SaasAuditEvent { WorkspaceId = row.WorkspaceId, Category = "lifecycle", Action = "operation.retry-requested", ActorId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueCommand<ProcessWorkspaceLifecycleCommand>(new ProcessWorkspaceLifecycle { RequestId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(CreateSupportAccessGrant request)
    {
        if (!config.EnableSupportAccess)
            throw new HttpError(409, "SupportAccessDisabled", "Temporary support access is disabled by deployment policy.");
        var session = await RequirePlatformAsync(PlatformCapability.ApproveSupportAccess);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        var supportOperator = await userManager.FindByIdAsync(request.OperatorId)
            ?? throw new HttpError(404, "SupportOperatorNotFound", "The selected support operator was not found.");
        if (!await userManager.IsInRoleAsync(supportOperator, "Support"))
            throw new HttpError(409, "SupportRoleRequired", "Support access can only be approved for a user with the Support role.");
        var now = DateTime.UtcNow;
        if (Db.Exists<SupportAccessGrant>(x => x.WorkspaceId == workspace.Id && x.OperatorId == supportOperator.Id &&
                x.RevokedAt == null && x.AccessEndedAt == null && x.ExpiresAt > now))
            throw new HttpError(409, "SupportAccessAlreadyActive", "This operator already has active support access for the organization.");
        var grant = new SupportAccessGrant {
            WorkspaceId = workspace.Id, OperatorId = supportOperator.Id, Capability = "ReadOnly",
            Reason = request.Reason.Trim(), StartsAt = now, ExpiresAt = now.AddMinutes(Math.Clamp(request.Minutes, 1, config.SupportAccessMaxMinutes)),
            CreatedDate = now, ModifiedDate = now, CreatedBy = session.UserAuthId!, ModifiedBy = session.UserAuthId!,
        };
        Db.Insert(grant);
        Db.Insert(new SaasAuditEvent { WorkspaceId = workspace.Id, Category = "support", Action = "access.granted", ActorId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason, DetailJson = new { grant.OperatorId, grant.ExpiresAt, grant.Capability }.ToJson(), CreatedDate = now });
        return grant;
    }

    public async Task<object> Any(RevokeSupportAccessGrant request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ApproveSupportAccess);
        var grant = Db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        grant.RevokedAt ??= DateTime.UtcNow;
        grant.ModifiedDate = DateTime.UtcNow;
        grant.ModifiedBy = session.UserAuthId!;
        Db.Update(grant);
        Db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.revoked", ActorId = session.UserAuthId!, SubjectId = grant.Id, CreatedDate = DateTime.UtcNow });
        return grant;
    }

    public async Task<object> Any(StartSupportAccess request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        if (PlatformAuthorization.HasRole(session, "Admin"))
            throw new HttpError(409, "SupportSessionNotRequired", "Administrators already have direct platform access and do not start support sessions.");
        var grant = Db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        if (!SupportAccessPolicy.IsUsable(grant, grant.WorkspaceId, session.UserAuthId!, DateTime.UtcNow, requireStarted: false))
            throw new HttpError(403, "SupportAccessUnavailable", "This support-access grant is not active for your account.");
        if (grant.AccessStartedAt == null)
        {
            grant.AccessStartedAt = DateTime.UtcNow;
            grant.ModifiedDate = DateTime.UtcNow;
            grant.ModifiedBy = session.UserAuthId!;
            Db.Update(grant);
            Db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.started", ActorId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason });
        }
        return grant;
    }

    public async Task<object> Any(EndSupportAccess request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var grant = Db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        if (grant.OperatorId != session.UserAuthId && !PlatformAuthorization.HasRole(session, "Admin"))
            throw new HttpError(403, "SupportAccessDenied", "This support-access grant belongs to another operator.");
        if (grant.AccessEndedAt == null)
        {
            grant.AccessEndedAt = DateTime.UtcNow;
            grant.ModifiedDate = DateTime.UtcNow;
            grant.ModifiedBy = session.UserAuthId!;
            Db.Update(grant);
            Db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.ended", ActorId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason });
        }
        return grant;
    }

    public async Task<object> Any(QueryPlatformOperators request)
    {
        await RequirePlatformAsync(PlatformCapability.ApproveSupportAccess);
        var users = await userManager.GetUsersInRoleAsync("Support");
        var results = new List<PlatformOperatorInfo>();
        foreach (var user in users.OrderBy(x => x.Email))
            results.Add(new PlatformOperatorInfo {
                UserId = user.Id, Email = user.Email ?? user.UserName ?? "", DisplayName = user.DisplayName ?? user.Email ?? "Support operator",
                Roles = (await userManager.GetRolesAsync(user)).ToList(),
            });
        return new QueryPlatformOperatorsResponse { Results = results };
    }

    public async Task<object> Any(GetNotificationPreferences request)
    {
        var context = await GetWorkspaceContextAsync();
        return new GetNotificationPreferencesResponse {
            Results = Db.Select<NotificationPreference>(x => x.UserId == context.UserId && x.WorkspaceId == context.Workspace.Id),
        };
    }

    public async Task<object> Any(UpdateNotificationPreferences request)
    {
        var context = await GetWorkspaceContextAsync();
        var now = DateTime.UtcNow;
        using (var tx = Db.OpenTransaction())
        {
            Db.Delete<NotificationPreference>(x => x.UserId == context.UserId && x.WorkspaceId == context.Workspace.Id);
            foreach (var input in request.Preferences ?? [])
                Db.Insert(new NotificationPreference {
                    UserId = context.UserId, WorkspaceId = context.Workspace.Id, TemplateKey = input.TemplateKey,
                    Channel = input.Channel, Enabled = input.Enabled,
                    CreatedDate = now, ModifiedDate = now, CreatedBy = context.UserId, ModifiedBy = context.UserId,
                });
            Audit(context, "notification", "preferences.updated", context.UserId);
            tx.Commit();
        }
        return new GetNotificationPreferencesResponse {
            Results = Db.Select<NotificationPreference>(x => x.UserId == context.UserId && x.WorkspaceId == context.Workspace.Id),
        };
    }

    public async Task<object> Any(QueryNotifications request)
    {
        var context = await GetWorkspaceContextAsync();
        var rows = Db.Select<NotificationDelivery>(x => x.UserId == context.UserId && x.WorkspaceId == context.Workspace.Id && x.Channel == NotificationChannel.InApp)
            .Where(x => request.UnreadOnly != true || x.ReadDate == null)
            .OrderByDescending(x => x.CreatedDate).ToList();
        return new QueryNotificationsResponse {
            Total = rows.Count,
            Results = rows.Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 100)).ToList(),
        };
    }

    public async Task<object> Any(MarkNotificationRead request)
    {
        var context = await GetWorkspaceContextAsync();
        var row = Db.Single<NotificationDelivery>(x => x.Id == request.Id && x.UserId == context.UserId && x.WorkspaceId == context.Workspace.Id)
            ?? throw new HttpError(404, "NotificationNotFound", "The notification was not found.");
        row.ReadDate ??= DateTime.UtcNow;
        row.ModifiedDate = DateTime.UtcNow;
        row.ModifiedBy = context.UserId;
        Db.Update(row);
        return new EmptyResponse();
    }

    public async Task<object> Any(QueryWorkspaceAuditEvents request)
    {
        var context = await GetWorkspaceContextAsync();
        if (!context.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
        var subscription = GetSubscription(context.Workspace.Id);
        if (!entitlements.HasFeature(Db, context.Workspace, subscription, "audit.read"))
            throw new HttpError(403, "FeatureNotEntitled", "Audit logs are not included in the current plan.");
        var rows = Db.Select<SaasAuditEvent>(x => x.WorkspaceId == context.Workspace.Id)
            .Where(x => request.Action.IsNullOrEmpty() || x.Action.Equals(request.Action, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedDate).ToList();
        return new QueryWorkspaceAuditEventsResponse {
            Total = rows.Count,
            Results = rows.Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 200)).ToList(),
        };
    }

    public async Task<object> Any(ExportWorkspaceAuditCsv request)
    {
        var context = await GetWorkspaceContextAsync();
        AssertAdmin(context);
        var subscription = GetSubscription(context.Workspace.Id);
        if (!entitlements.HasFeature(Db, context.Workspace, subscription, "audit.read"))
            throw new HttpError(403, "FeatureNotEntitled", "Audit logs are not included in the current plan.");

        var from = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(request.Days, 1, config.AnalyticsRetentionDays) - 1));
        var rows = Db.Select<SaasAuditEvent>(x => x.WorkspaceId == context.Workspace.Id && x.CreatedDate >= from)
            .Where(x => request.Action.IsNullOrEmpty() || x.Action.Equals(request.Action, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.CreatedDate);
        var csv = new StringBuilder("createdUtc,category,action,outcome,actor,subject,reason,requestId,ipAddress\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.CreatedDate.ToUniversalTime().ToString("O")), Csv(row.Category), Csv(row.Action),
                Csv(row.Outcome), Csv(row.ActorId), Csv(row.SubjectId), Csv(row.Reason), Csv(row.RequestId), Csv(row.IpAddress)));
        return CsvResult(csv.ToString(), $"audit-{context.Workspace.Slug}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(QueryPlatformAuditEvents request)
    {
        await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var rows = FilterPlatformAudit(request.Search, request.WorkspaceId, request.Category, request.Action,
            request.Outcome, request.From, request.To).OrderByDescending(x => x.CreatedDate).ToList();
        return new QueryWorkspaceAuditEventsResponse {
            Total = rows.Count,
            Results = rows.Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 250)).ToList(),
        };
    }

    public async Task<object> Any(ExportPlatformAuditCsv request)
    {
        await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var from = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(request.Days, 1, config.AnalyticsRetentionDays) - 1));
        var rows = FilterPlatformAudit(request.Search, request.WorkspaceId, request.Category, request.Action,
            request.Outcome, from, null).OrderBy(x => x.CreatedDate);
        var csv = new StringBuilder("createdUtc,workspaceId,category,action,outcome,actor,subject,reason,requestId,ipAddress\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.CreatedDate.ToUniversalTime().ToString("O")), Csv(row.WorkspaceId), Csv(row.Category), Csv(row.Action),
                Csv(row.Outcome), Csv(row.ActorId), Csv(row.SubjectId), Csv(row.Reason), Csv(row.RequestId), Csv(row.IpAddress)));
        return CsvResult(csv.ToString(), $"platform-audit-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(CreateSupportNote request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        if (!PlatformAuthorization.HasRole(session, "Admin") && GetActiveSupportAccess(workspace.Id, session.UserAuthId!, true) == null)
            throw new HttpError(403, "SupportAccessRequired", "An active support session is required to add a note for this organization.");
        var now = DateTime.UtcNow;
        var note = new SupportNote {
            WorkspaceId = workspace.Id, Body = request.Body.Trim(), CreatedDate = now, ModifiedDate = now,
            CreatedBy = session.UserAuthId!, ModifiedBy = session.UserAuthId!,
        };
        Db.Insert(note);
        Db.Insert(new SaasAuditEvent { WorkspaceId = workspace.Id, Category = "support", Action = "note.created", ActorId = session.UserAuthId!, SubjectId = note.Id, CreatedDate = now });
        return note;
    }

    public async Task<object> Any(ChangeWorkspaceStatus request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        RequireConfirmation(workspace, request.Confirmation);
        if (request.Status is WorkspaceStatus.PendingDeletion or WorkspaceStatus.Deleted)
            throw new HttpError(409, "LifecycleRequired", "Use the organization lifecycle workflow for deletion states.");
        var previous = workspace.Status;
        workspace.Status = request.Status;
        workspace.ModifiedDate = DateTime.UtcNow;
        workspace.ModifiedBy = session.UserAuthId!;
        Db.Update(workspace);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "workspace", Action = "status.changed", ActorId = session.UserAuthId!,
            SubjectId = workspace.Id, Reason = request.Reason.Trim(), DetailJson = new { previous, current = request.Status }.ToJson(), CreatedDate = DateTime.UtcNow,
        });
        return workspace;
    }

    public async Task<object> Any(PreviewSaasCustomerOperation request)
    {
        var capability = request.Operation == PlatformOperationType.BillingReconciliation
            ? PlatformCapability.ManageBilling : PlatformCapability.ManagePlatform;
        await RequirePlatformAsync(capability);
        var workspace = Db.SingleById<Workspace>(request.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        var subscription = GetSubscription(workspace.Id);
        var response = new PreviewSaasCustomerOperationResponse { Confirmation = workspace.Name };
        switch (request.Operation)
        {
            case PlatformOperationType.GaugeAdjustment:
                var current = manager.GetUsage(Db, workspace, subscription).FirstOrDefault(x => x.MeterKey == request.MeterKey)
                    ?? throw new HttpError(404, "MeterNotFound", "The selected meter was not found.");
                if (current.Kind != MeterKind.Gauge) throw new HttpError(409, "GaugeRequired", "Only gauge meters can be adjusted.");
                response.Title = $"Adjust {current.DisplayName}";
                response.Impact = [$"Current usage: {current.UsedUnits:N0}", $"Adjustment: {request.Delta ?? 0:+#,0;-#,0;0}", $"Result: {Math.Max(0, current.UsedUnits + (request.Delta ?? 0)):N0}"];
                if (current.UsedUnits + (request.Delta ?? 0) < 0) response.Warnings.Add("The result will be clamped to zero.");
                break;
            case PlatformOperationType.BillingReconciliation:
                response.Title = "Synchronize billing from Stripe";
                response.Impact = [$"Current local status: {subscription.Status}", $"Current access mode: {subscription.AccessMode}", "Stripe remains authoritative; local subscription and access fields may change."];
                if (subscription.StripeSubscriptionId.IsNullOrEmpty()) response.Warnings.Add("No local Stripe subscription ID is recorded.");
                break;
            case PlatformOperationType.WorkspaceStatusChange:
                response.Title = $"Change organization status to {request.Status}";
                response.Impact = [$"Current status: {workspace.Status}", $"Proposed status: {request.Status}", request.Status == WorkspaceStatus.Suspended ? "Customer product access will be blocked immediately." : "Normal status policy will be reevaluated."];
                if (request.Status is WorkspaceStatus.PendingDeletion or WorkspaceStatus.Deleted) response.Warnings.Add("Deletion states require the lifecycle workflow and cannot be applied here.");
                break;
        }
        return response;
    }

    public async Task<object> Any(CreateWorkspaceExport request)
    {
        var context = await GetWorkspaceContextAsync();
        AssertAdmin(context);
        var operation = NewLifecycle(context, LifecycleRequestType.Export, null, null);
        Db.Insert(operation);
        Audit(context, "lifecycle", "export.requested", operation.Id);
        jobs.EnqueueCommand<ProcessWorkspaceLifecycleCommand>(new ProcessWorkspaceLifecycle { RequestId = operation.Id });
        return operation;
    }

    public async Task<object> Any(RequestWorkspaceDeletion request)
    {
        var context = await GetWorkspaceContextAsync();
        if (context.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can request deletion.");
        var subscription = GetSubscription(context.Workspace.Id);
        if (subscription.Status is not (SubscriptionStatus.Free or SubscriptionStatus.Canceled))
            throw new HttpError(409, "ActiveSubscriptionMustBeCanceled", "Cancel the paid subscription from Billing before scheduling organization deletion.");
        if (!request.Confirmation.Equals(context.Workspace.Name, StringComparison.Ordinal))
            throw new HttpError(400, "DeletionConfirmationMismatch", "Enter the exact organization name to confirm deletion.");
        var retention = Db.SingleById<WorkspaceRetentionPolicy>(context.Workspace.Id);
        if (retention?.LegalHold == true)
            throw new HttpError(409, "WorkspaceLegalHold", "This organization is under a legal hold and cannot be deleted. Contact an administrator.");
        var existing = Db.Select<WorkspaceLifecycleRequest>(x => x.WorkspaceId == context.Workspace.Id && x.Type == LifecycleRequestType.Delete)
            .FirstOrDefault(x => x.Status is LifecycleRequestStatus.Pending or LifecycleRequestStatus.Scheduled or LifecycleRequestStatus.Processing);
        if (existing != null) return existing;
        var operation = NewLifecycle(context, LifecycleRequestType.Delete, request.Confirmation, null);
        operation.Status = LifecycleRequestStatus.Scheduled;
        operation.ScheduledAt = DateTime.UtcNow.AddDays(config.WorkspaceDeletionDelayDays);
        using var tx = Db.OpenTransaction();
        Db.Insert(operation);
        context.Workspace.Status = WorkspaceStatus.PendingDeletion;
        context.Workspace.ModifiedDate = DateTime.UtcNow;
        context.Workspace.ModifiedBy = context.UserId;
        Db.Update(context.Workspace);
        Audit(context, "lifecycle", "deletion.requested", operation.Id);
        tx.Commit();
        return operation;
    }

    public async Task<object> Any(CancelWorkspaceDeletion request)
    {
        var context = await GetWorkspaceContextAsync();
        if (context.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can cancel deletion.");
        var operation = Db.Select<WorkspaceLifecycleRequest>(x => x.WorkspaceId == context.Workspace.Id && x.Type == LifecycleRequestType.Delete)
            .Where(x => x.Status is LifecycleRequestStatus.Pending or LifecycleRequestStatus.Scheduled)
            .OrderByDescending(x => x.CreatedDate).FirstOrDefault()
            ?? throw new HttpError(404, "DeletionRequestNotFound", "There is no cancellable deletion request.");
        using var tx = Db.OpenTransaction();
        operation.Status = LifecycleRequestStatus.Canceled;
        operation.CompletedAt = DateTime.UtcNow;
        operation.ModifiedDate = DateTime.UtcNow;
        operation.ModifiedBy = context.UserId;
        Db.Update(operation);
        context.Workspace.Status = WorkspaceStatus.Active;
        context.Workspace.ModifiedDate = DateTime.UtcNow;
        context.Workspace.ModifiedBy = context.UserId;
        Db.Update(context.Workspace);
        Audit(context, "lifecycle", "deletion.canceled", operation.Id);
        tx.Commit();
        return operation;
    }

    public async Task<object> Any(TransferWorkspaceOwnership request)
    {
        var context = await GetWorkspaceContextAsync();
        if (context.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can transfer ownership.");
        var target = Db.Single<WorkspaceMember>(x => x.WorkspaceId == context.Workspace.Id && x.UserId == request.TargetUserId && x.Status == WorkspaceMemberStatus.Active)
            ?? throw new HttpError(404, "WorkspaceMemberNotFound", "The target user is not an active organization member.");
        using var tx = Db.OpenTransaction();
        context.Member.Role = WorkspaceMemberRole.Admin;
        context.Member.ModifiedDate = DateTime.UtcNow;
        context.Member.ModifiedBy = context.UserId;
        Db.Update(context.Member);
        target.Role = WorkspaceMemberRole.Owner;
        target.ModifiedDate = DateTime.UtcNow;
        target.ModifiedBy = context.UserId;
        Db.Update(target);
        Audit(context, "membership", "ownership.transferred", target.Id, new { from = context.UserId, to = target.UserId });
        tx.Commit();
        return new EmptyResponse();
    }

    public async Task<object> Any(LeaveWorkspace request)
    {
        var context = await GetWorkspaceContextAsync();
        if (context.Member.Role == WorkspaceMemberRole.Owner)
            throw new HttpError(409, "OwnershipTransferRequired", "Transfer ownership before leaving this organization.");
        using var tx = Db.OpenTransaction();
        Db.DeleteById<WorkspaceMember>(context.Member.Id);
        Audit(context, "membership", "member.left", context.Member.Id);
        tx.Commit();
        return new EmptyResponse();
    }

    public async Task<object> Any(GetWorkspaceLifecycle request)
    {
        var context = await GetWorkspaceContextAsync();
        AssertAdmin(context);
        return new GetWorkspaceLifecycleResponse {
            Results = Db.Select<WorkspaceLifecycleRequest>(x => x.WorkspaceId == context.Workspace.Id).OrderByDescending(x => x.CreatedDate).ToList(),
            Exports = Db.Select<DataExportArtifact>(x => x.WorkspaceId == context.Workspace.Id && x.ExpiresAt > DateTime.UtcNow && x.ExpiredAt == null).OrderByDescending(x => x.CreatedDate).ToList(),
            ExportExpiryDays = config.ExportExpiryDays,
            WorkspaceDeletionDelayDays = config.WorkspaceDeletionDelayDays,
        };
    }

    private async Task<WorkspaceContext> GetWorkspaceContextAsync()
    {
        var session = await GetSessionAsync();
        return workspaceContexts.Resolve(Db, session, Request);
    }

    private async Task<IAuthSession> RequirePlatformAsync(PlatformCapability capability)
    {
        var session = await GetSessionAsync();
        PlatformAuthorization.Require(session, capability);
        return session;
    }

    private SupportAccessGrant? GetActiveSupportAccess(string workspaceId, string operatorId, bool requireStarted)
    {
        var now = DateTime.UtcNow;
        return Db.Select<SupportAccessGrant>(x => x.WorkspaceId == workspaceId && x.OperatorId == operatorId &&
                x.RevokedAt == null && x.AccessEndedAt == null && x.StartsAt <= now && x.ExpiresAt > now)
            .Where(x => SupportAccessPolicy.IsUsable(x, workspaceId, operatorId, now, requireStarted))
            .OrderByDescending(x => x.ExpiresAt).FirstOrDefault();
    }

    private IEnumerable<SaasAuditEvent> FilterPlatformAudit(string? search, string? workspaceId, string? category,
        string? action, string? outcome, DateTime? from, DateTime? to)
    {
        var rows = Db.Select<SaasAuditEvent>();
        return rows.Where(x => workspaceId.IsNullOrEmpty() || x.WorkspaceId == workspaceId)
            .Where(x => category.IsNullOrEmpty() || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(x => action.IsNullOrEmpty() || x.Action.Contains(action!, StringComparison.OrdinalIgnoreCase))
            .Where(x => outcome.IsNullOrEmpty() || x.Outcome.Equals(outcome, StringComparison.OrdinalIgnoreCase))
            .Where(x => from == null || x.CreatedDate >= from.Value)
            .Where(x => to == null || x.CreatedDate <= to.Value)
            .Where(x => search.IsNullOrEmpty() || x.ActorId.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
                        x.SubjectId?.Contains(search!, StringComparison.OrdinalIgnoreCase) == true ||
                        x.Reason?.Contains(search!, StringComparison.OrdinalIgnoreCase) == true ||
                        x.RequestId?.Contains(search!, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static Workspace RedactWorkspace(Workspace workspace, bool includeBilling)
    {
        if (includeBilling) return workspace;
        return new Workspace {
            Id = workspace.Id, Name = workspace.Name, Slug = workspace.Slug, Status = workspace.Status,
            CreatedDate = workspace.CreatedDate, CreatedBy = workspace.CreatedBy,
            ModifiedDate = workspace.ModifiedDate, ModifiedBy = workspace.ModifiedBy,
        };
    }

    private static BillingSubscription RedactSubscription(BillingSubscription subscription, bool includeBilling)
    {
        if (includeBilling) return subscription;
        return new BillingSubscription {
            Id = subscription.Id, WorkspaceId = subscription.WorkspaceId, PlanVersionId = subscription.PlanVersionId,
            Status = subscription.Status, Interval = subscription.Interval, PeriodStart = subscription.PeriodStart,
            PeriodEnd = subscription.PeriodEnd, TrialEnd = subscription.TrialEnd, CancelAt = subscription.CancelAt,
            GraceEnd = subscription.GraceEnd, AccessMode = subscription.AccessMode, AccessReason = subscription.AccessReason,
            CreatedDate = subscription.CreatedDate, CreatedBy = subscription.CreatedBy,
            ModifiedDate = subscription.ModifiedDate, ModifiedBy = subscription.ModifiedBy,
        };
    }

    private static void RequireConfirmation(Workspace workspace, string confirmation)
    {
        if (!workspace.Name.Equals(confirmation?.Trim(), StringComparison.Ordinal))
            throw new HttpError(400, "OperationConfirmationMismatch", "Enter the exact organization name to confirm this operation.");
    }

    private BillingSubscription GetSubscription(string workspaceId) =>
        Db.Single<BillingSubscription>(x => x.WorkspaceId == workspaceId)
        ?? throw new HttpError(404, "SubscriptionNotFound", "The organization subscription was not found.");

    private static void AssertAdmin(WorkspaceContext context)
    {
        if (!context.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
    }

    private WorkspaceLifecycleRequest NewLifecycle(WorkspaceContext context, LifecycleRequestType type, string? confirmation, string? targetUserId)
    {
        var now = DateTime.UtcNow;
        return new WorkspaceLifecycleRequest {
            WorkspaceId = context.Workspace.Id, Type = type, Status = LifecycleRequestStatus.Pending,
            RequestedBy = context.UserId, Confirmation = confirmation, TargetUserId = targetUserId,
            CreatedDate = now, ModifiedDate = now, CreatedBy = context.UserId, ModifiedBy = context.UserId,
        };
    }

    private void Audit(WorkspaceContext context, string category, string action, string subjectId, object? detail = null) =>
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = context.Workspace.Id, Category = category, Action = action, ActorId = context.UserId,
            SubjectId = subjectId, DetailJson = detail?.ToJson(), RequestId = Request?.GetHeader("X-Request-Id"),
            IpAddress = Request?.RemoteIp, UserAgent = Request?.UserAgent, CreatedDate = DateTime.UtcNow,
        });

    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";

    private static HttpResult CsvResult(string csv, string fileName) => new(Encoding.UTF8.GetBytes(csv), "text/csv") {
        Headers = { [HttpHeaders.ContentDisposition] = $"attachment; filename=\"{fileName}\"" },
    };
}

public class ProcessWorkspaceLifecycle
{
    public string RequestId { get; set; } = "";
}
