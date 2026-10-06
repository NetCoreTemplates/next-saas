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

public static class WorkspaceAccessPolicy
{
    /// <summary>
    /// The access an API needs: what its [WorkspaceAccess] says, otherwise Read for GET APIs and Write for others
    /// </summary>
    public static WorkspaceAccess For(Type requestType, string? verb) =>
        requestType.FirstAttribute<WorkspaceAccessAttribute>()?.Access
        ?? (verb is null or HttpMethods.Get or HttpMethods.Head or HttpMethods.Options
            ? WorkspaceAccess.Read
            : WorkspaceAccess.Write);

    public static void Require(WorkspaceAccess access, Workspace workspace, BillingSubscription subscription)
    {
        if (access == WorkspaceAccess.Account) return;
        if (workspace.Status == WorkspaceStatus.Suspended || subscription.AccessMode == WorkspaceAccessMode.Suspended)
            throw new HttpError(403, "WorkspaceSuspended", "This organization is suspended. Contact support to restore access.");
        if (access == WorkspaceAccess.Write &&
            (workspace.Status != WorkspaceStatus.Active || subscription.AccessMode == WorkspaceAccessMode.ReadOnly))
            throw new HttpError(423, "WorkspaceReadOnly", "Changes are unavailable while the organization is not active.");
    }
}

public static class WorkspaceAuthorization
{
    public static void RequireAdmin(WorkspaceScope scope)
    {
        if (!scope.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
    }

    public static void RequireBilling(WorkspaceScope scope)
    {
        if (!scope.CanManageBilling)
            throw new HttpError(403, "BillingRoleRequired", "Organization Owner, Admin, or Billing role is required.");
    }

    public static void RequireOwner(WorkspaceScope scope)
    {
        if (scope.Member.Role != WorkspaceMemberRole.Owner)
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

/// <summary>
/// The scopes of APIs that can be called programmatically, and the API key a request was sent with
/// </summary>
public static class SaasApiKeys
{
    /// <summary>
    /// What an API key can be allowed to do. APIs say which they need with [ValidateHasScope].
    /// Signed-in users have all of them (see AdditionalUserClaimsPrincipalFactory), an API key has the ones
    /// it was created with.
    /// </summary>
    public static readonly string[] Scopes = ["usage:read", "usage:write", "workspace:read"];

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
    // Read on a connection confined to the organization, which only returns its overrides
    public List<EffectiveEntitlementInfo> GetEffective(IDbConnection db, Workspace workspace, BillingSubscription subscription)
    {
        db.AssertConfinedTo(workspace.Id);
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
        var overrides = db.Select<CustomerEntitlementOverride>()
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
    IEntitlementResolver entitlements,
    IStripeBillingGateway stripe,
    IBackgroundJobs jobs,
    UserManager<ApplicationUser> userManager) : Service
{
    public async Task<object> Any(GetEffectiveEntitlements request)
    {
        var scope = Db.GetWorkspaceScope();
        var subscription = Db.GetSubscription();
        manager.EvaluateAccess(Db, scope.Workspace, subscription);
        return new GetEffectiveEntitlementsResponse { Results = entitlements.GetEffective(Db, scope.Workspace, subscription) };
    }

    public async Task<object> Any(GetUsageAnalytics request)
    {
        var scope = Db.GetWorkspaceScope();
        var subscription = Db.GetSubscription();
        var usage = manager.GetUsage(Db, scope.Workspace, subscription);
        var selected = request.MeterKey.IsNullOrEmpty() ? usage.FirstOrDefault()?.MeterKey ?? "" : request.MeterKey!;
        if (!usage.Any(x => x.MeterKey == selected))
            throw new HttpError(404, "MeterNotFound", "The selected meter is not available to this organization.");
        var days = Math.Clamp(request.Days, 7, Math.Min(365, config.AnalyticsRetentionDays));
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rollups = Db.Select<UsageDailyRollup>(x => x.MeterKey == selected && x.Date >= from);
        var events = rollups.Count == 0
            ? Db.Select<UsageEvent>(x => x.MeterKey == selected && x.RecordedDate >= from)
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
        var rejected = Db.Count<SaasAuditEvent>(x => x.Category == "usage" && x.Action == "quota.rejected");
        return new GetUsageAnalyticsResponse {
            Usage = usage, MeterKey = selected, Series = series, ByUser = byUser,
            RejectedOperations = rejected, ProjectedPeriodEndUnits = projected,
        };
    }

    public async Task<object> Any(ExportUsageCsv request)
    {
        var scope = Db.GetWorkspaceScope();
        var subscription = Db.GetSubscription();
        var usage = manager.GetUsage(Db, scope.Workspace, subscription);
        var meterKey = request.MeterKey.IsNullOrEmpty() ? usage.FirstOrDefault()?.MeterKey ?? "" : request.MeterKey!;
        if (!usage.Any(x => x.MeterKey == meterKey))
            throw new HttpError(404, "MeterNotFound", "The selected meter is not available to this organization.");

        var days = Math.Clamp(request.Days, 1, Math.Min(365, config.AnalyticsRetentionDays));
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rows = Db.Select<UsageEvent>(x => x.MeterKey == meterKey && x.RecordedDate >= from)
            .OrderBy(x => x.RecordedDate);
        var csv = new StringBuilder("recordedUtc,meterKey,units,eventType,source,user,idempotencyKey\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.RecordedDate.ToUniversalTime().ToString("O")), Csv(row.MeterKey), row.Units,
                Csv(row.EventType), Csv(row.Source), Csv(row.RecordedBy), Csv(row.IdempotencyKey)));
        return CsvResult(csv.ToString(), $"usage-{meterKey}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(GetRevenueMetrics request)
    {
        await RequirePlatformAsync(PlatformCapability.ManageBilling);
        return SaasInsights.GetRevenueMetrics(PlatformDb, config.DefaultCurrency, Math.Clamp(request.Months, 1, 12), DateTime.UtcNow);
    }

    public async Task<object> Any(GetCustomerHealth request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var includeBilling = PlatformAuthorization.GetCapabilities(session).CanManageBilling;
        var customers = SaasInsights.GetCustomerHealth(PlatformDb, config.DefaultCurrency, DateTime.UtcNow);
        var matched = customers
            .Where(x => request.WorkspaceId.IsNullOrEmpty() || x.WorkspaceId == request.WorkspaceId)
            .Where(x => request.Grade == null ? request.IncludeChurned || !request.WorkspaceId.IsNullOrEmpty() || x.Grade != CustomerHealthGrade.Churned
                : x.Grade == request.Grade)
            .ToList();
        var response = new GetCustomerHealthResponse {
            Total = matched.Count, Currency = config.DefaultCurrency,
            Results = matched.Take(Math.Clamp(request.Take, 1, 200)).ToList(),
            Healthy = customers.Count(x => x.Grade == CustomerHealthGrade.Healthy),
            Watch = customers.Count(x => x.Grade == CustomerHealthGrade.Watch),
            AtRisk = customers.Count(x => x.Grade == CustomerHealthGrade.AtRisk),
            Churned = customers.Count(x => x.Grade == CustomerHealthGrade.Churned),
            MrrAtRisk = customers.Where(x => x.Grade == CustomerHealthGrade.AtRisk).Sum(x => x.Mrr),
        };
        // What customers pay is only shown to operators who can manage billing
        if (!includeBilling)
        {
            response.MrrAtRisk = 0;
            foreach (var customer in response.Results)
            {
                customer.Mrr = 0;
                customer.PaymentFailures = 0;
                foreach (var signal in customer.Signals.Where(x => x.Key == "billing.payment_failed"))
                    signal.Label = "Recent failed payments";
            }
        }
        return response;
    }

    public async Task<object> Any(GetSaasAnalytics request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var db = PlatformDb;
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        var now = DateTime.UtcNow;
        var days = Math.Clamp(request.Days, 7, 365);
        var from = now.Date.AddDays(-(days - 1));

        // Counted and grouped by the database, so the cost doesn't grow with the number or size of customers
        var paid = db.From<BillingSubscription>().Where(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trialing);
        // The same estimate the daily snapshot records, so the current value matches its history
        var estimatedMrr = SaasRevenue.Calculate(db, config.DefaultCurrency).Mrr;

        var versions = db.Select<SaasPlanVersion>().ToDictionary(x => x.Id);
        var plans = db.Select<SaasPlan>().ToDictionary(x => x.Id);
        var subscribersByVersion = db.Dictionary<string, long>(db.From<BillingSubscription>()
            .GroupBy(x => x.PlanVersionId)
            .Select(x => new { x.PlanVersionId, Count = Sql.Count("*") }));
        var planMix = subscribersByVersion
            .GroupBy(x => versions.TryGetValue(x.Key, out var version) && plans.TryGetValue(version.PlanId, out var plan) ? plan.Name : "Unknown")
            .Select(x => new UsageBreakdownItem { Key = x.Key, Label = x.Key, Units = x.Sum(y => y.Value) })
            .OrderByDescending(x => x.Units).ToList();

        // Only organizations created in the period are read, to chart when they were created
        var created = db.Column<DateTime>(db.From<Workspace>().Where(x => x.CreatedDate >= from).Select(x => x.CreatedDate))
            .GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.LongCount());
        var growth = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(date => new UsageSeriesPoint {
            Date = date, Units = created.GetValueOrDefault(date),
        }).ToList();

        var pressureIds = SaasInsights.QuotaPressure(db, now, take: 25);
        var pressure = pressureIds.Count == 0 ? [] : db.Select<Workspace>(x => Sql.In(x.Id, pressureIds));

        var response = new GetSaasAnalyticsResponse {
            Metrics = [
                new() { Key = "workspaces.active", Label = "Active organizations", Value = db.Count<Workspace>(x => x.Status == WorkspaceStatus.Active) },
                new() { Key = "subscriptions.paid", Label = "Paid subscriptions", Value = db.Count(paid) },
                new() { Key = "subscriptions.trialing", Label = "Active trials", Value = db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Trialing) },
                new() { Key = "subscriptions.past_due", Label = "Past due", Value = db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.PastDue) },
                new() { Key = "subscriptions.canceling", Label = "Canceling", Value = db.Count<BillingSubscription>(x => x.CancelAt != null && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trialing)) },
                new() { Key = "revenue.mrr_estimate", Label = $"Estimated MRR ({config.DefaultCurrency.ToUpperInvariant()})", Value = estimatedMrr, Format = $"currency:{config.DefaultCurrency.ToUpperInvariant()}" },
                new() { Key = "usage.events", Label = "Usage events", Value = db.Count<UsageEvent>(x => x.RecordedDate >= from) },
                new() { Key = "usage.rejections", Label = "Quota rejections", Value = db.Count<SaasAuditEvent>(x => x.Category == "usage" && x.Action == "quota.rejected" && x.CreatedDate >= from) },
                new() { Key = "storage.files", Label = "Available files", Value = db.Count<StoredFile>(x => x.Status == StoredFileStatus.Available) },
                new() { Key = "operations.stripe_failed", Label = "Failed Stripe events", Value = db.Count<StripeEventInbox>(x => x.Status == StripeInboxStatus.Failed) },
            ],
            WorkspaceGrowth = growth, PlanMix = planMix,
            QuotaPressure = pressure.Select(x => RedactWorkspace(x, capabilities.CanManageBilling)).ToList(),
        };
        if (!capabilities.CanManageBilling)
            response.Metrics.RemoveAll(x => x.Key.StartsWith("revenue.") || x.Key.StartsWith("subscriptions."));
        return response;
    }

    public async Task<object> Any(GetSaasCustomer request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var workspace = RequireCustomer(request.WorkspaceId);
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        SupportAccessGrant? supportAccess = null;
        if (PlatformAuthorization.HasRole(session, "Support") && !PlatformAuthorization.HasRole(session, "Admin"))
        {
            supportAccess = GetActiveSupportAccess(Db, workspace.Id, session.UserAuthId!, requireStarted: true);
            if (supportAccess == null)
            {
                Db.Insert(new SaasAuditEvent {
                    Category = "support", Action = "access.denied", Outcome = "Rejected",
                    UserId = session.UserAuthId!, SubjectId = workspace.Id, Reason = "No active, started support-access grant.",
                    RequestId = Request?.GetHeader("X-Request-Id"), IpAddress = Request?.RemoteIp, UserAgent = Request?.UserAgent,
                });
                throw new HttpError(403, "SupportAccessRequired", "Start an approved support-access session before viewing this organization.");
            }
        }
        var subscription = Db.GetSubscription();
        manager.EvaluateAccess(Db, workspace, subscription);
        var details = new SaasCustomerDetails {
            Workspace = RedactWorkspace(workspace, capabilities.CanManageBilling),
            Subscription = RedactSubscription(subscription, capabilities.CanManageBilling),
            Plan = manager.GetPlanInfo(Db, subscription.PlanVersionId),
            Entitlements = entitlements.GetEffective(Db, workspace, subscription),
            Overrides = Db.Select(Db.From<CustomerEntitlementOverride>().OrderBy(x => x.Key)),
            Usage = manager.GetUsage(Db, workspace, subscription),
            Members = Db.Select<WorkspaceMember>().Select(x => new WorkspaceMemberInfo {
                Id = x.Id, UserId = x.UserId, Email = x.InvitedEmail, Role = x.Role, Status = x.Status,
                InvitationEmailSent = x.InvitationSentDate != null, JoinedDate = x.JoinedDate,
            }).ToList(),
            Files = Db.Select(Db.From<StoredFile>().Where(x => x.Status != StoredFileStatus.Deleted)
                .OrderByDescending(x => x.CreatedDate).Limit(25)).Select(x => new StoredFileInfo {
                    Id = x.Id, Name = x.Name, ContentType = x.ContentType, ByteLength = x.ByteLength,
                    Sha256 = x.Sha256, Status = x.Status, CreatedDate = x.CreatedDate, UploadedBy = x.UploadedBy,
                }).ToList(),
            SupportNotes = Db.Select(Db.From<SupportNote>().OrderByDescending(x => x.CreatedDate).Limit(50)),
            AuditEvents = Db.Select(Db.From<SaasAuditEvent>().OrderByDescending(x => x.CreatedDate).Limit(100)),
            Notifications = Db.Select(Db.From<NotificationDelivery>().OrderByDescending(x => x.CreatedDate).Limit(50)),
            LifecycleRequests = Db.Select(Db.From<WorkspaceLifecycleRequest>().OrderByDescending(x => x.CreatedDate).Limit(25)),
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
        var db = PlatformDb;
        var capabilities = PlatformAuthorization.GetCapabilities(session);
        var q = db.From<Workspace>().Where(x => x.Status != WorkspaceStatus.Deleted);

        // Customers are searched and paged by the database: only the page that's returned is loaded
        var matchedOn = new Dictionary<string, string>();
        var search = request.Search?.Trim().ToLowerInvariant();
        if (!search.IsNullOrEmpty())
        {
            // The first reason a customer matches is the one that's shown
            void Match(string reason, IEnumerable<string?> workspaceIds)
            {
                foreach (var workspaceId in workspaceIds)
                {
                    if (!workspaceId.IsNullOrEmpty()) matchedOn.TryAdd(workspaceId!, reason);
                }
            }
            List<string> WorkspaceIds(System.Linq.Expressions.Expression<Func<Workspace, bool>> where) =>
                db.Column<string>(db.From<Workspace>().Where(where).Select(x => x.Id));

            Match("organization name", WorkspaceIds(x => x.Name.ToLower().Contains(search!)));
            Match("slug", WorkspaceIds(x => x.Slug.ToLower().Contains(search!)));
            Match("billing email", WorkspaceIds(x => x.BillingEmail!.ToLower().Contains(search!)));
            Match("Stripe customer", WorkspaceIds(x => x.StripeCustomerId!.ToLower().Contains(search!)));
            Match("Stripe subscription", db.Column<string>(db.From<BillingSubscription>()
                .Where(x => x.StripeSubscriptionId!.ToLower().Contains(search!)).Select(x => x.WorkspaceId)));
            Match("member email", db.Column<string>(db.From<WorkspaceMember>()
                .Where(x => x.InvitedEmail!.ToLower().Contains(search!)).Select(x => x.WorkspaceId)));
            if (db.TableExists<User>())
            {
                var userIds = db.Column<string>(db.From<User>()
                    .Where(x => x.Email!.ToLower().Contains(search!) || x.UserName.ToLower().Contains(search!)).Select(x => x.Id));
                if (userIds.Count > 0)
                    Match("member email", db.Column<string>(db.From<WorkspaceMember>()
                        .Where(x => Sql.In(x.UserId, userIds)).Select(x => x.WorkspaceId)));
            }
            if (db.TableExists<ApiKeysFeature.ApiKey>())
                Match("API-key fingerprint", db.Column<string>(db.From<ApiKeysFeature.ApiKey>()
                    .Where(x => x.VisibleKey!.ToLower().Contains(search!)).Select(x => x.RefIdStr)));

            if (matchedOn.Count == 0)
                return new QuerySaasCustomersResponse();
            q.And(x => Sql.In(x.Id, matchedOn.Keys));
        }

        var total = db.Count(q);
        var workspaces = db.Select(q.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
            .Limit(Math.Max(0, request.Skip), Math.Clamp(request.Take, 1, 100)));
        var workspaceIds = workspaces.Select(x => x.Id).ToList();
        var subscriptions = workspaceIds.Count == 0 ? [] : db.Select<BillingSubscription>(x => Sql.In(x.WorkspaceId, workspaceIds))
            .ToDictionary(x => x.WorkspaceId);
        var versions = db.Select<SaasPlanVersion>().ToDictionary(x => x.Id);
        var plans = db.Select<SaasPlan>().ToDictionary(x => x.Id);

        return new QuerySaasCustomersResponse {
            Total = total,
            Results = workspaces.Select(workspace => {
                subscriptions.TryGetValue(workspace.Id, out var subscription);
                var planName = subscription != null && versions.TryGetValue(subscription.PlanVersionId, out var version) && plans.TryGetValue(version.PlanId, out var plan)
                    ? plan.Name : "Unknown";
                return new SaasCustomerSummary {
                    WorkspaceId = workspace.Id, Name = workspace.Name, Slug = workspace.Slug, Status = workspace.Status,
                    BillingEmail = capabilities.CanManageBilling ? workspace.BillingEmail : null,
                    StripeCustomerId = capabilities.CanManageBilling ? workspace.StripeCustomerId : null,
                    StripeSubscriptionId = capabilities.CanManageBilling ? subscription?.StripeSubscriptionId : null,
                    SubscriptionStatus = subscription?.Status ?? SubscriptionStatus.Free,
                    PlanName = planName, MatchedOn = matchedOn.GetValueOrDefault(workspace.Id, "recent organization"),
                };
            }).ToList(),
        };
    }

    public async Task<object> Any(GetSaasOperations request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ViewCustomers);
        var db = PlatformDb;
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
            PendingReservations = capabilities.CanManagePlatform ? db.Select(db.From<UsageReservation>().Where(x => x.Status == UsageReservationStatus.Pending).OrderBy(x => x.ExpiresAt).Limit(100)) : [],
            FailedStripeEvents = capabilities.CanManageBilling ? db.Select(db.From<StripeEventInbox>().Where(x => x.Status == StripeInboxStatus.Failed).OrderByDescending(x => x.ReceivedDate).Limit(100)) : [],
            FailedNotifications = capabilities.CanManageSupport ? db.Select(db.From<NotificationDelivery>().Where(x => x.Status == NotificationDeliveryStatus.Failed).OrderByDescending(x => x.ModifiedDate).Limit(100)) : [],
            ActiveLifecycleRequests = capabilities.CanManagePlatform ? db.Select(db.From<WorkspaceLifecycleRequest>()
                .Where(x => x.Status != LifecycleRequestStatus.Completed && x.Status != LifecycleRequestStatus.Canceled)
                .OrderByDescending(x => x.CreatedDate).Limit(100)) : [],
            ActiveSupportAccess = db.Select<SupportAccessGrant>(x => x.RevokedAt == null && x.AccessEndedAt == null && x.StartsAt <= now && x.ExpiresAt > now)
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
            RetentionRuns = capabilities.CanManagePlatform && db.TableExists<DataRetentionRun>()
                ? db.Select(db.From<DataRetentionRun>().OrderByDescending(x => x.CreatedDate).Limit(10)) : [],
        };
    }

    public async Task<object> Any(UpdateWorkspaceRetentionPolicy request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var workspace = RequireCustomer(request.WorkspaceId);
        if (request.LegalHold && !config.EnableLegalHolds)
            throw new HttpError(409, "LegalHoldsDisabled", "Legal holds are disabled by global configuration.");
        foreach (var days in new[] { request.AnalyticsRetentionDays, request.AuditRetentionDays,
                     request.NotificationRetentionDays, request.DeletedFileRetentionDays, request.LifecycleHistoryRetentionDays })
            if (days is < 1 or > 3650)
                throw new HttpError(400, "InvalidRetentionPeriod", "Customer retention periods must be between 1 and 3650 days.");
        var now = DateTime.UtcNow;
        var row = Db.SingleById<WorkspaceRetentionPolicy>(workspace.Id) ?? new WorkspaceRetentionPolicy {
            WorkspaceId = workspace.Id,
        };
        row.AnalyticsRetentionDays = request.AnalyticsRetentionDays;
        row.AuditRetentionDays = request.AuditRetentionDays;
        row.NotificationRetentionDays = request.NotificationRetentionDays;
        row.DeletedFileRetentionDays = request.DeletedFileRetentionDays;
        row.LifecycleHistoryRetentionDays = request.LifecycleHistoryRetentionDays;
        row.LegalHold = request.LegalHold;
        row.Reason = request.Reason.Trim();
        Db.Save(row);
        Db.Insert(new SaasAuditEvent {
            Category = "lifecycle", Action = "retention.policy-updated",
            UserId = session.UserAuthId!, SubjectId = workspace.Id, Reason = row.Reason,
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
        var workspace = RequireCustomer(request.WorkspaceId);
        RequireConfirmation(workspace, request.Confirmation);
        var subscription = Db.GetSubscription();
        var meter = config.Meters.LastOrDefault(x => x.Key.Equals(request.MeterKey, StringComparison.OrdinalIgnoreCase));
        if (meter == null || meter.Kind != MeterKind.Gauge)
            throw new HttpError(409, "GaugeRequired", "Only gauge meters can be corrected by an operator.");

        var result = manager.AdjustGauge(Db, workspace, subscription, session.UserAuthId!, request.MeterKey,
            request.Delta, request.IdempotencyKey, "operator-correction");
        Db.Insert(new SaasAuditEvent {
            Category = "usage", Action = "gauge.adjusted", UserId = session.UserAuthId!,
            SubjectId = request.MeterKey, Reason = request.Reason.Trim(),
            DetailJson = new { request.Delta, request.IdempotencyKey, result.UsedUnits }.ToJson(), CreatedDate = DateTime.UtcNow,
        });
        return result;
    }

    public async Task<object> Any(ReconcileSaasCustomerBilling request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageBilling);
        var workspace = RequireCustomer(request.WorkspaceId);
        RequireConfirmation(workspace, request.Confirmation);
        if (!await stripe.ReconcileSubscriptionAsync(Db, workspace))
            throw new HttpError(409, "StripeSubscriptionNotFound", "This organization does not have a Stripe subscription to reconcile.");
        var subscription = Db.GetSubscription();
        manager.EvaluateAccess(Db, workspace, subscription);
        Db.Insert(new SaasAuditEvent {
            Category = "billing",
            Action = "subscription.reconciled",
            UserId = session.UserAuthId!,
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
        Db.Insert(new PlatformAuditEvent { Category = "stripe", Action = "event.retry-requested", UserId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueCommand<ProcessStripeEventCommand>(new ProcessStripeEvent { InboxId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(RetryNotificationDelivery request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        // The delivery is found by its id in any organization, then the request is confined to its organization
        var workspaceId = PlatformDb.Scalar<string>(PlatformDb.From<NotificationDelivery>()
                .Where(x => x.Id == request.Id).Select(x => x.WorkspaceId))
            ?? throw new HttpError(404, "NotificationNotFound", "The notification delivery was not found.");
        RequireCustomer(workspaceId);
        if (!PlatformAuthorization.HasRole(session, "Admin") && GetActiveSupportAccess(Db, workspaceId, session.UserAuthId!, true) == null)
            throw new HttpError(403, "SupportAccessRequired", "An active support session is required to retry this organization's notification.");
        var row = Db.SingleById<NotificationDelivery>(request.Id);
        row.Status = NotificationDeliveryStatus.Pending;
        row.LastError = null;
        Db.Update(row);
        Db.Insert(new SaasAuditEvent { Category = "notification", Action = "delivery.retry-requested", UserId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueForWorkspace<ProcessNotificationDeliveryCommand>(workspaceId,
            new ProcessNotificationDelivery { WorkspaceId = workspaceId, DeliveryId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(RetryWorkspaceLifecycle request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var db = PlatformDb;
        var row = db.SingleById<WorkspaceLifecycleRequest>(request.Id)
            ?? throw new HttpError(404, "LifecycleRequestNotFound", "The lifecycle request was not found.");
        if (row.Status != LifecycleRequestStatus.Failed)
            throw new HttpError(409, "LifecycleRequestNotFailed", "Only failed lifecycle requests can be retried.");
        row.Status = LifecycleRequestStatus.Pending;
        row.LastError = null;
        db.Update(row);
        db.Insert(new SaasAuditEvent { WorkspaceId = row.WorkspaceId, Category = "lifecycle", Action = "operation.retry-requested", UserId = session.UserAuthId!, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        jobs.EnqueueForWorkspace<ProcessWorkspaceLifecycleCommand>(row.WorkspaceId, new ProcessWorkspaceLifecycle { WorkspaceId = row.WorkspaceId, RequestId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(CreateSupportAccessGrant request)
    {
        if (!config.EnableSupportAccess)
            throw new HttpError(409, "SupportAccessDisabled", "Temporary support access is disabled by deployment policy.");
        var session = await RequirePlatformAsync(PlatformCapability.ApproveSupportAccess);
        var workspace = RequireCustomer(request.WorkspaceId);
        var supportOperator = await userManager.FindByIdAsync(request.OperatorId)
            ?? throw new HttpError(404, "SupportOperatorNotFound", "The selected support operator was not found.");
        if (!await userManager.IsInRoleAsync(supportOperator, "Support"))
            throw new HttpError(409, "SupportRoleRequired", "Support access can only be approved for a user with the Support role.");
        var now = DateTime.UtcNow;
        if (Db.Exists<SupportAccessGrant>(x => x.OperatorId == supportOperator.Id &&
                x.RevokedAt == null && x.AccessEndedAt == null && x.ExpiresAt > now))
            throw new HttpError(409, "SupportAccessAlreadyActive", "This operator already has active support access for the organization.");
        var grant = new SupportAccessGrant {
            OperatorId = supportOperator.Id, Capability = "ReadOnly",
            Reason = request.Reason.Trim(), StartsAt = now, ExpiresAt = now.AddMinutes(Math.Clamp(request.Minutes, 1, config.SupportAccessMaxMinutes)),
        };
        Db.Insert(grant);
        Db.Insert(new SaasAuditEvent { Category = "support", Action = "access.granted", UserId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason, DetailJson = new { grant.OperatorId, grant.ExpiresAt, grant.Capability }.ToJson(), CreatedDate = now });
        return grant;
    }

    public async Task<object> Any(RevokeSupportAccessGrant request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ApproveSupportAccess);
        var db = PlatformDb;
        var grant = db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        grant.RevokedAt ??= DateTime.UtcNow;
        db.Update(grant);
        db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.revoked", UserId = session.UserAuthId!, SubjectId = grant.Id, CreatedDate = DateTime.UtcNow });
        return grant;
    }

    public async Task<object> Any(StartSupportAccess request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var db = PlatformDb;
        if (PlatformAuthorization.HasRole(session, "Admin"))
            throw new HttpError(409, "SupportSessionNotRequired", "Administrators already have direct platform access and do not start support sessions.");
        var grant = db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        if (!SupportAccessPolicy.IsUsable(grant, grant.WorkspaceId, session.UserAuthId!, DateTime.UtcNow, requireStarted: false))
            throw new HttpError(403, "SupportAccessUnavailable", "This support-access grant is not active for your account.");
        if (grant.AccessStartedAt == null)
        {
            grant.AccessStartedAt = DateTime.UtcNow;
            db.Update(grant);
            db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.started", UserId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason });
        }
        return grant;
    }

    public async Task<object> Any(EndSupportAccess request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var db = PlatformDb;
        var grant = db.SingleById<SupportAccessGrant>(request.Id)
            ?? throw new HttpError(404, "SupportAccessNotFound", "The support access grant was not found.");
        if (grant.OperatorId != session.UserAuthId && !PlatformAuthorization.HasRole(session, "Admin"))
            throw new HttpError(403, "SupportAccessDenied", "This support-access grant belongs to another operator.");
        if (grant.AccessEndedAt == null)
        {
            grant.AccessEndedAt = DateTime.UtcNow;
            db.Update(grant);
            db.Insert(new SaasAuditEvent { WorkspaceId = grant.WorkspaceId, Category = "support", Action = "access.ended", UserId = session.UserAuthId!, SubjectId = grant.Id, Reason = grant.Reason });
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
        var scope = Db.GetWorkspaceScope();
        return new GetNotificationPreferencesResponse {
            Results = Db.Select<NotificationPreference>(x => x.UserId == scope.UserId),
        };
    }

    public async Task<object> Any(UpdateNotificationPreferences request)
    {
        var scope = Db.GetWorkspaceScope();
        using (var tx = Db.OpenTransaction())
        {
            Db.Delete<NotificationPreference>(x => x.UserId == scope.UserId);
            foreach (var input in request.Preferences ?? [])
                Db.Insert(new NotificationPreference {
                    UserId = scope.UserId, TemplateKey = input.TemplateKey,
                    Channel = input.Channel, Enabled = input.Enabled,
                });
            Audit(scope, "notification", "preferences.updated", scope.UserId);
            tx.Commit();
        }
        return new GetNotificationPreferencesResponse {
            Results = Db.Select<NotificationPreference>(x => x.UserId == scope.UserId),
        };
    }

    public async Task<object> Any(QueryNotifications request)
    {
        var scope = Db.GetWorkspaceScope();
        var q = Db.From<NotificationDelivery>().Where(x => x.UserId == scope.UserId && x.Channel == NotificationChannel.InApp);
        if (request.UnreadOnly == true)
            q.And(x => x.ReadDate == null);
        return new QueryNotificationsResponse {
            Total = Db.Count(q),
            Results = Db.Select(q.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
                .Limit(Math.Max(0, request.Skip), Math.Clamp(request.Take, 1, 100))),
        };
    }

    public async Task<object> Any(MarkNotificationRead request)
    {
        var scope = Db.GetWorkspaceScope();
        var row = Db.Single<NotificationDelivery>(x => x.Id == request.Id && x.UserId == scope.UserId)
            ?? throw new HttpError(404, "NotificationNotFound", "The notification was not found.");
        row.ReadDate ??= DateTime.UtcNow;
        Db.Update(row);
        return new EmptyResponse();
    }

    public async Task<object> Any(QueryWorkspaceAuditEvents request)
    {
        var scope = Db.GetWorkspaceScope();
        if (!scope.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
        var subscription = Db.GetSubscription();
        if (!entitlements.HasFeature(Db, scope.Workspace, subscription, "audit.read"))
            throw new HttpError(403, "FeatureNotEntitled", "Audit logs are not included in the current plan.");
        var q = AuditQuery(Db, request.Action);
        var total = Db.Count(q);

        // Pages continue after the last event that was seen, so they stay fast however far back they go, and
        // events recorded since the first page don't shift the pages after it
        q.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id).Take(Math.Clamp(request.Take, 1, 200));
        if (!request.AfterId.IsNullOrEmpty())
        {
            var last = Db.SingleById<SaasAuditEvent>(request.AfterId)
                ?? throw new HttpError(404, "AuditEventNotFound", "The audit event to continue after was not found.");
            q.SeekAfter(last);
        }
        return new QueryWorkspaceAuditEventsResponse { Total = total, Results = Db.Select(q) };
    }

    public async Task<object> Any(ExportWorkspaceAuditCsv request)
    {
        var scope = Db.GetWorkspaceScope();
        AssertAdmin(scope);
        var subscription = Db.GetSubscription();
        if (!entitlements.HasFeature(Db, scope.Workspace, subscription, "audit.read"))
            throw new HttpError(403, "FeatureNotEntitled", "Audit logs are not included in the current plan.");

        var from = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(request.Days, 1, config.AnalyticsRetentionDays) - 1));
        var rows = Db.Select(AuditQuery(Db, request.Action).And(x => x.CreatedDate >= from).OrderBy(x => x.CreatedDate));
        var csv = new StringBuilder("createdUtc,category,action,outcome,user,subject,reason,requestId,ipAddress\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.CreatedDate.ToUniversalTime().ToString("O")), Csv(row.Category), Csv(row.Action),
                Csv(row.Outcome), Csv(row.UserId), Csv(row.SubjectId), Csv(row.Reason), Csv(row.RequestId), Csv(row.IpAddress)));
        return CsvResult(csv.ToString(), $"audit-{scope.Workspace.Slug}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(QueryPlatformAuditEvents request)
    {
        await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 250);
        var (total, rows) = QueryPlatformAudit(request.Search, request.WorkspaceId, request.Category, request.Action,
            request.Outcome, request.From, request.To, newestFirst: true, limit: skip + take);
        return new QueryPlatformAuditEventsResponse {
            Total = total,
            Results = rows.Skip(skip).Take(take).ToList(),
        };
    }

    public async Task<object> Any(ExportPlatformAuditCsv request)
    {
        await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var from = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(request.Days, 1, config.AnalyticsRetentionDays) - 1));
        var (_, rows) = QueryPlatformAudit(request.Search, request.WorkspaceId, request.Category, request.Action,
            request.Outcome, from, null, newestFirst: false, limit: null);
        var csv = new StringBuilder("createdUtc,workspaceId,category,action,outcome,user,subject,reason,requestId,ipAddress\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', Csv(row.CreatedDate.ToUniversalTime().ToString("O")), Csv(row.WorkspaceId), Csv(row.Category), Csv(row.Action),
                Csv(row.Outcome), Csv(row.UserId), Csv(row.SubjectId), Csv(row.Reason), Csv(row.RequestId), Csv(row.IpAddress)));
        return CsvResult(csv.ToString(), $"platform-audit-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<object> Any(CreateSupportNote request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManageSupport);
        var workspace = RequireCustomer(request.WorkspaceId);
        if (!PlatformAuthorization.HasRole(session, "Admin") && GetActiveSupportAccess(Db, workspace.Id, session.UserAuthId!, true) == null)
            throw new HttpError(403, "SupportAccessRequired", "An active support session is required to add a note for this organization.");
        var now = DateTime.UtcNow;
        var note = new SupportNote { Body = request.Body.Trim() };
        Db.Insert(note);
        Db.Insert(new SaasAuditEvent { Category = "support", Action = "note.created", UserId = session.UserAuthId!, SubjectId = note.Id, CreatedDate = now });
        return note;
    }

    public async Task<object> Any(ChangeWorkspaceStatus request)
    {
        var session = await RequirePlatformAsync(PlatformCapability.ManagePlatform);
        var workspace = RequireCustomer(request.WorkspaceId);
        RequireConfirmation(workspace, request.Confirmation);
        if (request.Status is WorkspaceStatus.PendingDeletion or WorkspaceStatus.Deleted)
            throw new HttpError(409, "LifecycleRequired", "Use the organization lifecycle workflow for deletion states.");
        var previous = workspace.Status;
        workspace.Status = request.Status;
        Db.Update(workspace);
        Db.Insert(new SaasAuditEvent {
            Category = "workspace", Action = "status.changed", UserId = session.UserAuthId!,
            SubjectId = workspace.Id, Reason = request.Reason.Trim(), DetailJson = new { previous, current = request.Status }.ToJson(), CreatedDate = DateTime.UtcNow,
        });
        return workspace;
    }

    public async Task<object> Any(PreviewSaasCustomerOperation request)
    {
        var capability = request.Operation == PlatformOperationType.BillingReconciliation
            ? PlatformCapability.ManageBilling : PlatformCapability.ManagePlatform;
        await RequirePlatformAsync(capability);
        var workspace = RequireCustomer(request.WorkspaceId);
        var subscription = Db.GetSubscription();
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
        var scope = Db.GetWorkspaceScope();
        AssertAdmin(scope);
        var operation = NewLifecycle(scope, LifecycleRequestType.Export, null, null);
        Db.Insert(operation);
        Audit(scope, "lifecycle", "export.requested", operation.Id);
        jobs.EnqueueForWorkspace<ProcessWorkspaceLifecycleCommand>(scope.Workspace.Id, new ProcessWorkspaceLifecycle { WorkspaceId = scope.Workspace.Id, RequestId = operation.Id });
        return operation;
    }

    public async Task<object> Any(RequestWorkspaceDeletion request)
    {
        var scope = Db.GetWorkspaceScope();
        if (scope.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can request deletion.");
        var subscription = Db.GetSubscription();
        if (subscription.Status is not (SubscriptionStatus.Free or SubscriptionStatus.Canceled))
            throw new HttpError(409, "ActiveSubscriptionMustBeCanceled", "Cancel the paid subscription from Billing before scheduling organization deletion.");
        if (!request.Confirmation.Equals(scope.Workspace.Name, StringComparison.Ordinal))
            throw new HttpError(400, "DeletionConfirmationMismatch", "Enter the exact organization name to confirm deletion.");
        var retention = Db.SingleById<WorkspaceRetentionPolicy>(scope.Workspace.Id);
        if (retention?.LegalHold == true)
            throw new HttpError(409, "WorkspaceLegalHold", "This organization is under a legal hold and cannot be deleted. Contact an administrator.");
        var existing = Db.Select<WorkspaceLifecycleRequest>(x => x.Type == LifecycleRequestType.Delete)
            .FirstOrDefault(x => x.Status is LifecycleRequestStatus.Pending or LifecycleRequestStatus.Scheduled or LifecycleRequestStatus.Processing);
        if (existing != null) return existing;
        var operation = NewLifecycle(scope, LifecycleRequestType.Delete, request.Confirmation, null);
        operation.Status = LifecycleRequestStatus.Scheduled;
        operation.ScheduledAt = DateTime.UtcNow.AddDays(config.WorkspaceDeletionDelayDays);
        using var tx = Db.OpenTransaction();
        Db.Insert(operation);
        scope.Workspace.Status = WorkspaceStatus.PendingDeletion;
        Db.Update(scope.Workspace);
        Audit(scope, "lifecycle", "deletion.requested", operation.Id);
        tx.Commit();
        return operation;
    }

    public async Task<object> Any(CancelWorkspaceDeletion request)
    {
        var scope = Db.GetWorkspaceScope();
        if (scope.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can cancel deletion.");
        var operation = Db.Select<WorkspaceLifecycleRequest>(x => x.Type == LifecycleRequestType.Delete)
            .Where(x => x.Status is LifecycleRequestStatus.Pending or LifecycleRequestStatus.Scheduled)
            .OrderByDescending(x => x.CreatedDate).FirstOrDefault()
            ?? throw new HttpError(404, "DeletionRequestNotFound", "There is no cancellable deletion request.");
        using var tx = Db.OpenTransaction();
        operation.Status = LifecycleRequestStatus.Canceled;
        operation.CompletedAt = DateTime.UtcNow;
        Db.Update(operation);
        scope.Workspace.Status = WorkspaceStatus.Active;
        Db.Update(scope.Workspace);
        Audit(scope, "lifecycle", "deletion.canceled", operation.Id);
        tx.Commit();
        return operation;
    }

    public async Task<object> Any(TransferWorkspaceOwnership request)
    {
        var scope = Db.GetWorkspaceScope();
        if (scope.Member.Role != WorkspaceMemberRole.Owner)
            throw new HttpError(403, "WorkspaceOwnerRequired", "Only the organization Owner can transfer ownership.");
        var target = Db.Single<WorkspaceMember>(x => x.UserId == request.TargetUserId && x.Status == WorkspaceMemberStatus.Active)
            ?? throw new HttpError(404, "WorkspaceMemberNotFound", "The target user is not an active organization member.");
        using var tx = Db.OpenTransaction();
        scope.Member.Role = WorkspaceMemberRole.Admin;
        Db.Update(scope.Member);
        target.Role = WorkspaceMemberRole.Owner;
        Db.Update(target);
        Audit(scope, "membership", "ownership.transferred", target.Id, new { from = scope.UserId, to = target.UserId });
        tx.Commit();
        return new EmptyResponse();
    }

    public async Task<object> Any(LeaveWorkspace request)
    {
        var scope = Db.GetWorkspaceScope();
        if (scope.Member.Role == WorkspaceMemberRole.Owner)
            throw new HttpError(409, "OwnershipTransferRequired", "Transfer ownership before leaving this organization.");
        using var tx = Db.OpenTransaction();
        Db.DeleteById<WorkspaceMember>(scope.Member.Id);
        Audit(scope, "membership", "member.left", scope.Member.Id);
        tx.Commit();
        return new EmptyResponse();
    }

    public async Task<object> Any(GetWorkspaceLifecycle request)
    {
        var scope = Db.GetWorkspaceScope();
        AssertAdmin(scope);
        return new GetWorkspaceLifecycleResponse {
            Results = Db.Select<WorkspaceLifecycleRequest>().OrderByDescending(x => x.CreatedDate).ToList(),
            Exports = Db.Select<DataExportArtifact>(x => x.ExpiresAt > DateTime.UtcNow && x.ExpiredAt == null).OrderByDescending(x => x.CreatedDate).ToList(),
            ExportExpiryDays = config.ExportExpiryDays,
            WorkspaceDeletionDelayDays = config.WorkspaceDeletionDelayDays,
        };
    }


    private async Task<IAuthSession> RequirePlatformAsync(PlatformCapability capability)
    {
        var session = await GetSessionAsync();
        PlatformAuthorization.Require(session, capability);
        return session;
    }

    /// <summary>
    /// Platform APIs that work across customers, used after RequirePlatformAsync() has checked the capability
    /// </summary>
    private IDbConnection PlatformDb => Db.AcrossWorkspaces();

    /// <summary>
    /// Platform APIs that work on one customer confine the request to that organization, so the rest of the
    /// API can only read and write that customer's data
    /// </summary>
    private Workspace RequireCustomer(string workspaceId)
    {
        Db.ForWorkspace(workspaceId);
        return Db.SingleById<Workspace>(workspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
    }

    private static SupportAccessGrant? GetActiveSupportAccess(IDbConnection db, string workspaceId, string operatorId, bool requireStarted)
    {
        db.AssertConfinedTo(workspaceId);
        var now = DateTime.UtcNow;
        return db.Select<SupportAccessGrant>(x => x.OperatorId == operatorId &&
                x.RevokedAt == null && x.AccessEndedAt == null && x.StartsAt <= now && x.ExpiresAt > now)
            .Where(x => SupportAccessPolicy.IsUsable(x, workspaceId, operatorId, now, requireStarted))
            .OrderByDescending(x => x.ExpiresAt).FirstOrDefault();
    }

    // Conditions are applied by the database. Text is matched without regard to case on every provider.
    private static SqlExpression<SaasAuditEvent> AuditQuery(IDbConnection db, string? action) =>
        AuditQuery<SaasAuditEvent>(db, null, null, action, partOfAction: false, null, null, null);

    private static SqlExpression<T> AuditQuery<T>(IDbConnection db, string? search, string? category, string? action,
        bool partOfAction, string? outcome, DateTime? from, DateTime? to) where T : AuditEventBase
    {
        var q = db.From<T>();
        if (!category.IsNullOrEmpty())
        {
            var value = category!.Trim().ToLowerInvariant();
            q.And(x => x.Category.ToLower() == value);
        }
        if (!action.IsNullOrEmpty())
        {
            var value = action!.Trim().ToLowerInvariant();
            if (partOfAction)
                q.And(x => x.Action.ToLower().Contains(value));
            else
                q.And(x => x.Action.ToLower() == value);
        }
        if (!outcome.IsNullOrEmpty())
        {
            var value = outcome!.Trim().ToLowerInvariant();
            q.And(x => x.Outcome.ToLower() == value);
        }
        if (from != null)
            q.And(x => x.CreatedDate >= from.Value);
        if (to != null)
            q.And(x => x.CreatedDate <= to.Value);
        if (!search.IsNullOrEmpty())
        {
            var term = search!.Trim().ToLowerInvariant();
            q.And(x => x.UserId.ToLower().Contains(term) || x.SubjectId!.ToLower().Contains(term) ||
                       x.Reason!.ToLower().Contains(term) || x.RequestId!.ToLower().Contains(term));
        }
        return q;
    }

    /// <summary>
    /// The audit logs of every organization and of the platform, for operators. They're two tables, so each is
    /// queried for the rows that could be among the first <paramref name="limit"/>, which are then merged.
    /// </summary>
    private (long Total, List<AuditEventInfo> Rows) QueryPlatformAudit(string? search, string? workspaceId, string? category,
        string? action, string? outcome, DateTime? from, DateTime? to, bool newestFirst, int? limit)
    {
        var db = PlatformDb;
        SqlExpression<T> Query<T>() where T : AuditEventBase
        {
            var q = AuditQuery<T>(db, search, category, action, partOfAction: true, outcome, from, to);
            q = newestFirst
                ? q.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
                : q.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id);
            return q;
        }

        var organizations = Query<SaasAuditEvent>();
        if (!workspaceId.IsNullOrEmpty())
            organizations.And(x => x.WorkspaceId == workspaceId);
        var total = db.Count(organizations);
        var rows = db.Select(limit != null ? organizations.Limit(limit.Value) : organizations).ConvertAll(x => x.ConvertTo<AuditEventInfo>());

        // The platform's events aren't about an organization
        if (workspaceId.IsNullOrEmpty())
        {
            var platform = Query<PlatformAuditEvent>();
            total += db.Count(platform);
            rows.AddRange(db.Select(limit != null ? platform.Limit(limit.Value) : platform).ConvertAll(x => x.ConvertTo<AuditEventInfo>()));
        }

        var ordered = newestFirst
            ? rows.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
            : rows.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id);
        return (total, (limit != null ? ordered.Take(limit.Value) : ordered).ToList());
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

    private static void AssertAdmin(WorkspaceScope scope)
    {
        if (!scope.IsAdmin)
            throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
    }

    private WorkspaceLifecycleRequest NewLifecycle(WorkspaceScope scope, LifecycleRequestType type, string? confirmation, string? targetUserId)
    {
        return new WorkspaceLifecycleRequest {
            Type = type, Status = LifecycleRequestStatus.Pending,
            RequestedBy = scope.UserId, Confirmation = confirmation, TargetUserId = targetUserId,
        };
    }

    private void Audit(WorkspaceScope scope, string category, string action, string subjectId, object? detail = null) =>
        Db.Insert(new SaasAuditEvent {
            Category = category, Action = action, UserId = scope.UserId,
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
    public string WorkspaceId { get; set; } = default!;
    public string RequestId { get; set; } = default!;
}
