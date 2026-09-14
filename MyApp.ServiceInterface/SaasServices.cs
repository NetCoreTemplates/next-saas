using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public record StripeWebhookEnvelope(string EventId, string EventType, string PayloadJson);

public static class WorkspaceInvitationTokens
{
    public static string Create()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static void AssertCanAccept(WorkspaceMember member, string? signedInEmail, DateTime now)
    {
        if (member.Status != WorkspaceMemberStatus.Invited || member.InvitationRevokedDate != null)
            throw new HttpError(409, "InvitationNotPending", "This invitation is no longer pending.");
        if (member.InvitationExpiresAt == null || member.InvitationExpiresAt <= now)
            throw new HttpError(410, "InvitationExpired", "This invitation has expired. Ask an organization administrator to resend it.");
        if (signedInEmail.IsNullOrEmpty() || !string.Equals(signedInEmail!.Trim(), member.InvitedEmail, StringComparison.OrdinalIgnoreCase))
            throw new HttpError(403, "InvitationEmailMismatch", $"Sign in as {member.InvitedEmail} to accept this invitation.");
    }
}

public interface IStripeBillingGateway
{
    bool IsConfigured { get; }
    bool IsLiveMode { get; }
    bool IsCatalogProvisioningEnabled { get; }
    Task<List<SaasCouponInfo>> GetCouponsAsync(CancellationToken token = default);
    Task<SaasCouponInfo> CreateCouponAsync(CreateSaasCoupon request, CancellationToken token = default);
    Task<SaasCouponInfo> DeactivateCouponAsync(string promotionCodeId, CancellationToken token = default);
    Task<ProvisionSaasPlanStripeCatalogResponse> ProvisionCatalogAsync(SaasPlan plan, ProvisionSaasPlanStripeCatalog request, CancellationToken token = default);
    Task<string> CreateCheckoutAsync(Workspace workspace, SaasPlanPrice price, string successUrl, string cancelUrl, int? trialDays, CancellationToken token = default);
    Task<bool> ConfirmCheckoutAsync(IDbConnection db, Workspace workspace, string? sessionId, CancellationToken token = default);
    Task<string> CreatePortalAsync(Workspace workspace, string returnUrl, CancellationToken token = default);
    Task<bool> ReconcileSubscriptionAsync(IDbConnection db, Workspace workspace, CancellationToken token = default);
    StripeWebhookEnvelope ValidateWebhook(string payload, string signature);
    Task ApplyWebhookAsync(IDbConnection db, StripeEventInbox inbox, CancellationToken token = default);
}

public interface ISaasManager
{
    Workspace EnsurePersonalWorkspace(IDbConnection db, string userId, string? displayName, string? email);
    Workspace CreateOrganization(IDbConnection db, string userId, string name, string? billingEmail);
    PlanInfo GetPlanInfo(IDbConnection db, string planVersionId);
    void EvaluateAccess(IDbConnection db, Workspace workspace, BillingSubscription subscription);
    List<UsageSummary> GetUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription);
    RecordUsageResponse RecordUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, RecordUsage request);
    UsageReservation ReserveUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, string meterKey, long units, string idempotencyKey, string? metadataJson = null);
    UsageSummary SettleUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, string reservationId, long actualUnits);
    UsageSummary ReleaseUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, string reservationId);
    UsageSummary AdjustGauge(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, string meterKey, long delta, string idempotencyKey, string source);
    SaasPlanDetails GetPlanDetails(IDbConnection db, string planId);
    SaasPlanDetails SavePlanDraft(IDbConnection db, string actorId, SaveSaasPlanDraft request);
    SaasPlanDetails PublishPlanDraft(IDbConnection db, string actorId, string planId);
}

public class SaasManager(SaasConfig config) : ISaasManager
{
    public void EvaluateAccess(IDbConnection db, Workspace workspace, BillingSubscription subscription) => ApplyLifecyclePolicy(db, workspace, subscription);
    public Workspace EnsurePersonalWorkspace(IDbConnection db, string userId, string? displayName, string? email)
    {
        var member = FindActiveMembership(db, userId);
        if (member != null)
        {
            SaveWorkspacePreference(db, userId, member.WorkspaceId);
            BindLegacyApiKeys(db, userId, member.WorkspaceId);
            return db.SingleById<Workspace>(member.WorkspaceId);
        }
        if (!config.EnablePersonalWorkspaces)
            throw new HttpError(403, "WorkspaceMembershipRequired", "Ask an organization administrator to invite you before continuing.");

        var name = displayName.IsNullOrEmpty() ? email?.LeftPart('@') ?? "My organization" : displayName!;
        var slugBase = Slugify(name);
        var plan = db.Single<SaasPlan>(x => x.Code == config.DefaultPlan && !x.IsArchived)
            ?? throw new HttpError(404, "DefaultPlanNotFound", $"Default plan '{config.DefaultPlan}' is not configured.");
        var version = db.Single<SaasPlanVersion>(x => x.PlanId == plan.Id && x.Status == PlanVersionStatus.Published)
            ?? throw new HttpError(404, "DefaultPlanVersionNotFound", $"Plan '{config.DefaultPlan}' has no published version.");

        // The shell and page can request workspace state concurrently on a user's first visit.
        // Let the database arbitrate slug/member uniqueness, then adopt the workspace created by
        // the winning request. Retrying also gives a different user racing for the same slug a
        // chance to allocate the next suffix. This remains safe across multiple app instances.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            member = FindActiveMembership(db, userId);
            if (member != null)
            {
                SaveWorkspacePreference(db, userId, member.WorkspaceId);
                BindLegacyApiKeys(db, userId, member.WorkspaceId);
                return db.SingleById<Workspace>(member.WorkspaceId);
            }

            var slug = slugBase;
            var suffix = 1;
            while (db.Exists<Workspace>(x => x.Slug == slug))
                slug = $"{slugBase}-{++suffix}";

            var now = DateTime.UtcNow;
            var workspace = new Workspace {
                Name = name, Slug = slug, BillingEmail = email,
                CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
            };
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var subscription = new BillingSubscription {
                WorkspaceId = workspace.Id, PlanVersionId = version.Id, Status = SubscriptionStatus.Free,
                PeriodStart = periodStart, PeriodEnd = periodStart.AddMonths(1),
                CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
            };

            try
            {
                using (var tx = db.OpenTransaction())
                {
                    db.Insert(workspace);
                    db.Insert(new WorkspaceMember {
                        WorkspaceId = workspace.Id, UserId = userId, Role = WorkspaceMemberRole.Owner,
                        Status = WorkspaceMemberStatus.Active, JoinedDate = now,
                        CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
                    });
                    db.Insert(new UserWorkspacePreference {
                        UserId = userId, ActiveWorkspaceId = workspace.Id,
                        CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
                    });
                    db.Insert(subscription);
                    db.Insert(new SaasAuditEvent {
                        WorkspaceId = workspace.Id, Category = "workspace", Action = "created",
                        ActorId = userId, SubjectId = workspace.Id, CreatedDate = now,
                    });
                    tx.Commit();
                }
                BindLegacyApiKeys(db, userId, workspace.Id);
                return workspace;
            }
            catch (Exception ex) when (IsWorkspaceCreationRace(ex))
            {
                Thread.Sleep(20 * (attempt + 1));
            }
        }

        member = FindActiveMembership(db, userId);
        if (member != null)
        {
            SaveWorkspacePreference(db, userId, member.WorkspaceId);
            BindLegacyApiKeys(db, userId, member.WorkspaceId);
            return db.SingleById<Workspace>(member.WorkspaceId);
        }
        throw new HttpError(409, "WorkspaceCreationConflict", "The organization could not be created because another request is still changing this account. Please retry.");
    }

    public Workspace CreateOrganization(IDbConnection db, string userId, string name, string? billingEmail)
    {
        var normalizedName = name.Trim();
        var slugBase = Slugify(normalizedName);
        var slug = slugBase;
        var suffix = 1;
        while (db.Exists<Workspace>(x => x.Slug == slug))
            slug = $"{slugBase}-{++suffix}";

        var plan = db.Single<SaasPlan>(x => x.Code == config.DefaultPlan && !x.IsArchived)
            ?? throw new HttpError(404, "DefaultPlanNotFound", $"Default plan '{config.DefaultPlan}' is not configured.");
        var version = db.Select<SaasPlanVersion>(x => x.PlanId == plan.Id && x.Status == PlanVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefault()
            ?? throw new HttpError(404, "DefaultPlanVersionNotFound", $"Plan '{config.DefaultPlan}' has no published version.");
        var now = DateTime.UtcNow;
        var workspace = new Workspace {
            Name = normalizedName, Slug = slug, BillingEmail = billingEmail?.Trim(),
            CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
        };
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        using var tx = db.OpenTransaction();
        db.Insert(workspace);
        db.Insert(new WorkspaceMember {
            WorkspaceId = workspace.Id, UserId = userId, Role = WorkspaceMemberRole.Owner,
            Status = WorkspaceMemberStatus.Active, JoinedDate = now,
            CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
        });
        db.Insert(new BillingSubscription {
            WorkspaceId = workspace.Id, PlanVersionId = version.Id, Status = SubscriptionStatus.Free,
            PeriodStart = periodStart, PeriodEnd = periodStart.AddMonths(1),
            CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
        });
        SaveWorkspacePreference(db, userId, workspace.Id);
        db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "workspace", Action = "created",
            ActorId = userId, SubjectId = workspace.Id, CreatedDate = now,
        });
        tx.Commit();
        return workspace;
    }

    private static WorkspaceMember? FindActiveMembership(IDbConnection db, string userId)
    {
        var preference = db.SingleById<UserWorkspacePreference>(userId);
        var member = preference == null ? null : db.Single<WorkspaceMember>(x => x.WorkspaceId == preference.ActiveWorkspaceId &&
            x.UserId == userId && x.Status == WorkspaceMemberStatus.Active);
        return member ?? db.Select<WorkspaceMember>(x => x.UserId == userId && x.Status == WorkspaceMemberStatus.Active)
            .OrderBy(x => x.Role == WorkspaceMemberRole.Owner ? 0 : 1).ThenBy(x => x.CreatedDate).FirstOrDefault();
    }

    private static bool IsWorkspaceCreationRace(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate entry", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("serialization failure", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void SaveWorkspacePreference(IDbConnection db, string userId, string workspaceId)
    {
        var now = DateTime.UtcNow;
        var preference = db.SingleById<UserWorkspacePreference>(userId);
        if (preference?.ActiveWorkspaceId == workspaceId) return;
        preference ??= new UserWorkspacePreference { UserId = userId, CreatedDate = now, CreatedBy = userId };
        preference.ActiveWorkspaceId = workspaceId;
        preference.ModifiedDate = now;
        preference.ModifiedBy = userId;
        db.Save(preference);
    }

    private static void BindLegacyApiKeys(IDbConnection db, string userId, string workspaceId)
    {
        if (!db.TableExists<ApiKeysFeature.ApiKey>()) return;
        foreach (var apiKey in db.Select<ApiKeysFeature.ApiKey>(x => x.UserId == userId && x.RefIdStr == null))
        {
            apiKey.RefIdStr = workspaceId;
            db.Update(apiKey);
        }
    }

    public PlanInfo GetPlanInfo(IDbConnection db, string planVersionId)
    {
        var version = db.SingleById<SaasPlanVersion>(planVersionId)
            ?? throw new HttpError(404, "PlanVersionNotFound", "The selected plan version was not found.");
        var plan = db.SingleById<SaasPlan>(version.PlanId);
        var features = db.Select<SaasPlanFeature>(x => x.PlanVersionId == version.Id && x.Enabled).OrderBy(x => x.DisplayOrder);
        var quotas = db.Select<SaasPlanQuota>(x => x.PlanVersionId == version.Id);
        var prices = db.Select<SaasPlanPrice>(x => x.PlanVersionId == version.Id && x.IsActive);
        return new PlanInfo {
            Id = version.Id, Code = plan.Code, Name = version.Name ?? plan.Name,
            Description = version.Description ?? plan.Description,
            IsContactSales = version.IsContactSales ?? plan.IsContactSales, TrialDays = version.TrialDays,
            Features = features.Select(x => x.Name).ToList(),
            Quotas = quotas.Select(x => new PlanQuotaInfo {
                MeterKey = x.MeterKey, DisplayName = x.DisplayName, IncludedUnits = x.IncludedUnits,
                Enforcement = x.Enforcement, RolloverEnabled = x.RolloverEnabled,
            }).ToList(),
            Prices = prices.Select(x => new PlanPriceInfo {
                Id = x.Id, Currency = x.Currency, Interval = x.Interval, UnitAmount = x.UnitAmount,
                CheckoutReady = !x.StripePriceId.IsNullOrEmpty(),
            }).ToList(),
        };
    }

    public List<UsageSummary> GetUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription)
    {
        ApplyLifecyclePolicy(db, workspace, subscription);
        EnsureCurrentFreePeriod(db, subscription);
        var plan = GetPlanInfo(db, GetEffectivePlanVersionId(db, subscription));
        var now = DateTime.UtcNow;
        var results = new List<UsageSummary>();
        foreach (var quota in plan.Quotas)
        {
            var activeOverride = db.Single<CustomerEntitlementOverride>(x => x.WorkspaceId == workspace.Id && x.Key == quota.MeterKey
                && (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidUntil == null || x.ValidUntil > now));
            var allowance = activeOverride?.QuotaUnits ?? quota.IncludedUnits;
            var usagePeriod = EnsureUsagePeriod(db, workspace.Id, subscription, quota, allowance,
                activeOverride == null ? "plan" : "customer-override");
            var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == usagePeriod.Id);
            if (quota.MeterKey == "workspace.seats")
            {
                aggregate.UsedUnits = db.Count<WorkspaceMember>(x => x.WorkspaceId == workspace.Id && x.Status != WorkspaceMemberStatus.Disabled);
                aggregate.ModifiedDate = now;
                aggregate.ModifiedBy = "membership-projection";
                db.Update(aggregate);
            }
            results.Add(ToSummary(usagePeriod, aggregate, quota.DisplayName));
        }
        return results;
    }

    public RecordUsageResponse RecordUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription, string userId, RecordUsage request)
    {
        var duplicate = db.Single<UsageEvent>(x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == request.IdempotencyKey);
        if (duplicate != null)
        {
            var duplicatePeriod = db.SingleById<UsagePeriod>(duplicate.UsagePeriodId);
            var duplicateAggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == duplicatePeriod.Id);
            var displayName = GetPlanInfo(db, GetEffectivePlanVersionId(db, subscription)).Quotas
                .FirstOrDefault(x => x.MeterKey == duplicate.MeterKey)?.DisplayName ?? duplicate.MeterKey;
            return new RecordUsageResponse { Accepted = true, Duplicate = true, Usage = ToSummary(duplicatePeriod, duplicateAggregate, displayName) };
        }

        var usage = GetUsage(db, workspace, subscription).FirstOrDefault(x => x.MeterKey == request.MeterKey)
            ?? throw new HttpError(404, "MeterNotFound", $"Meter '{request.MeterKey}' is not defined by the effective plan.");
        if (usage.Kind == MeterKind.Gauge)
            throw new HttpError(409, "GaugeMutationRequired", $"Meter '{request.MeterKey}' is maintained by product operations and cannot be incremented through the public usage API.");
        if (usage.Allowance != null && usage.Enforcement == QuotaEnforcement.HardLimit && usage.UsedUnits + request.Units > usage.Allowance)
            throw QuotaExceeded(db, workspace, userId, usage, request.Units, "consume");

        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == workspace.Id && x.MeterKey == request.MeterKey
            && x.PeriodStart == usage.PeriodStart);
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        var now = DateTime.UtcNow;
        using var tx = db.OpenTransaction();
        db.Insert(new UsageEvent {
            WorkspaceId = workspace.Id, UsagePeriodId = period.Id, MeterKey = request.MeterKey,
            Units = request.Units, IdempotencyKey = request.IdempotencyKey, Source = "api",
            MetadataJson = request.MetadataJson, RecordedDate = now, RecordedBy = userId,
        });
        var updated = db.ExecuteSql(@"UPDATE UsageAggregate
SET UsedUnits = UsedUnits + @Units,
    PeakUnits = CASE WHEN PeakUnits > UsedUnits + @Units THEN PeakUnits ELSE UsedUnits + @Units END,
    LastEventDate = @Now, ModifiedDate = @Now, ModifiedBy = @UserId
WHERE Id = @Id
  AND (@HardLimit = 0 OR @Allowance IS NULL OR UsedUnits + ReservedUnits + @Units <= @Allowance)",
            new { request.Units, Now = now, UserId = userId, aggregate.Id,
                HardLimit = period.Enforcement == QuotaEnforcement.HardLimit ? 1 : 0, period.Allowance });
        if (updated != 1)
            throw new HttpError(429, "QuotaExceeded", $"This request would exceed the {usage.DisplayName} allowance.");
        aggregate = db.SingleById<UsageAggregate>(aggregate.Id);
        if (period.Allowance is > 0)
        {
            var previousPercent = (aggregate.UsedUnits - request.Units) * 100d / period.Allowance.Value;
            var currentPercent = aggregate.UsedUnits * 100d / period.Allowance.Value;
            foreach (var threshold in config.QuotaWarningPercentages.Where(x => previousPercent < x && currentPercent >= x))
                db.Insert(new SaasAuditEvent { WorkspaceId=workspace.Id, Category="usage", Action="quota.warning", ActorId="quota-policy", SubjectId=period.Id, DetailJson=new { meterKey=request.MeterKey, threshold, used=aggregate.UsedUnits, allowance=period.Allowance }.ToJson(), CreatedDate=now });
        }
        tx.Commit();
        SaasTelemetry.UsageRecorded.Add(request.Units,
            new KeyValuePair<string, object?>("meter", request.MeterKey));
        return new RecordUsageResponse { Accepted = true, Usage = ToSummary(period, aggregate, usage.DisplayName) };
    }

    public UsageReservation ReserveUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription,
        string userId, string meterKey, long units, string idempotencyKey, string? metadataJson = null)
    {
        if (units <= 0) throw new HttpError(400, "InvalidReservationUnits", "Reserved units must be greater than zero.");
        var duplicate = db.Single<UsageReservation>(x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey);
        if (duplicate != null) return duplicate;

        var usage = GetUsage(db, workspace, subscription).FirstOrDefault(x => x.MeterKey == meterKey)
            ?? throw new HttpError(404, "MeterNotFound", $"Meter '{meterKey}' is not defined by the effective plan.");
        if (usage.Allowance != null && usage.Enforcement == QuotaEnforcement.HardLimit && usage.UsedUnits + usage.ReservedUnits + units > usage.Allowance)
            throw QuotaExceeded(db, workspace, userId, usage, units, "reserve");

        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == workspace.Id && x.MeterKey == meterKey && x.PeriodStart == usage.PeriodStart);
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        var now = DateTime.UtcNow;
        var reservation = new UsageReservation {
            WorkspaceId = workspace.Id, UsagePeriodId = period.Id, MeterKey = meterKey,
            ReservedUnits = units, IdempotencyKey = idempotencyKey, ExpiresAt = now.AddMinutes(30),
            MetadataJson = metadataJson, CreatedDate = now, ModifiedDate = now, CreatedBy = userId, ModifiedBy = userId,
        };
        using var tx = db.OpenTransaction();
        db.Insert(reservation);
        var updated = db.ExecuteSql(@"UPDATE UsageAggregate
SET ReservedUnits = ReservedUnits + @Units, ModifiedDate = @Now, ModifiedBy = @UserId
WHERE Id = @Id
  AND (@HardLimit = 0 OR @Allowance IS NULL OR UsedUnits + ReservedUnits + @Units <= @Allowance)",
            new { Units = units, Now = now, UserId = userId, aggregate.Id,
                HardLimit = usage.Enforcement == QuotaEnforcement.HardLimit ? 1 : 0, usage.Allowance });
        if (updated != 1)
            throw new HttpError(429, "QuotaExceeded", $"This operation would exceed the {usage.DisplayName} allowance.");
        tx.Commit();
        return reservation;
    }

    public UsageSummary SettleUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription,
        string userId, string reservationId, long actualUnits)
    {
        if (actualUnits < 0) throw new HttpError(400, "InvalidSettlementUnits", "Settled units cannot be negative.");
        var reservation = db.SingleById<UsageReservation>(reservationId)
            ?? throw new HttpError(404, "UsageReservationNotFound", "The usage reservation was not found.");
        if (reservation.WorkspaceId != workspace.Id)
            throw new HttpError(403, "WorkspaceAccessDenied", "The reservation belongs to another organization.");
        var period = db.SingleById<UsagePeriod>(reservation.UsagePeriodId);
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        var displayName = GetPlanInfo(db, GetEffectivePlanVersionId(db, subscription)).Quotas.FirstOrDefault(x => x.MeterKey == period.MeterKey)?.DisplayName ?? period.MeterKey;
        if (reservation.Status == UsageReservationStatus.Settled)
            return ToSummary(period, aggregate, displayName);
        if (reservation.Status != UsageReservationStatus.Pending)
            throw new HttpError(409, "UsageReservationClosed", "The usage reservation is no longer pending.");
        if (actualUnits > reservation.ReservedUnits && period.Allowance != null && period.Enforcement == QuotaEnforcement.HardLimit &&
            aggregate.UsedUnits + aggregate.ReservedUnits - reservation.ReservedUnits + actualUnits > period.Allowance)
            throw new HttpError(429, "QuotaExceeded", "The actual usage exceeds the remaining allowance.");

        var now = DateTime.UtcNow;
        using var tx = db.OpenTransaction();
        aggregate.ReservedUnits = Math.Max(0, aggregate.ReservedUnits - reservation.ReservedUnits);
        aggregate.UsedUnits += actualUnits;
        aggregate.PeakUnits = Math.Max(aggregate.PeakUnits, aggregate.UsedUnits);
        aggregate.LastEventDate = now;
        aggregate.ModifiedDate = now;
        aggregate.ModifiedBy = userId;
        db.Update(aggregate);
        reservation.Status = UsageReservationStatus.Settled;
        reservation.SettledUnits = actualUnits;
        reservation.SettledDate = now;
        reservation.ModifiedDate = now;
        reservation.ModifiedBy = userId;
        db.Update(reservation);
        if (actualUnits != 0)
            db.Insert(new UsageEvent {
                WorkspaceId = workspace.Id, UsagePeriodId = period.Id, MeterKey = period.MeterKey,
                Units = actualUnits, IdempotencyKey = $"reservation:{reservation.Id}:settled", Source = "reservation",
                EventType = "settle", MetadataJson = reservation.MetadataJson, RecordedDate = now, RecordedBy = userId,
            });
        tx.Commit();
        return ToSummary(period, aggregate, displayName);
    }

    public UsageSummary ReleaseUsage(IDbConnection db, Workspace workspace, BillingSubscription subscription,
        string userId, string reservationId)
    {
        var reservation = db.SingleById<UsageReservation>(reservationId)
            ?? throw new HttpError(404, "UsageReservationNotFound", "The usage reservation was not found.");
        if (reservation.WorkspaceId != workspace.Id)
            throw new HttpError(403, "WorkspaceAccessDenied", "The reservation belongs to another organization.");
        var period = db.SingleById<UsagePeriod>(reservation.UsagePeriodId);
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        var displayName = GetPlanInfo(db, GetEffectivePlanVersionId(db, subscription)).Quotas.FirstOrDefault(x => x.MeterKey == period.MeterKey)?.DisplayName ?? period.MeterKey;
        if (reservation.Status != UsageReservationStatus.Pending)
            return ToSummary(period, aggregate, displayName);
        var now = DateTime.UtcNow;
        using var tx = db.OpenTransaction();
        aggregate.ReservedUnits = Math.Max(0, aggregate.ReservedUnits - reservation.ReservedUnits);
        aggregate.ModifiedDate = now;
        aggregate.ModifiedBy = userId;
        db.Update(aggregate);
        reservation.Status = UsageReservationStatus.Released;
        reservation.ModifiedDate = now;
        reservation.ModifiedBy = userId;
        db.Update(reservation);
        tx.Commit();
        return ToSummary(period, aggregate, displayName);
    }

    public UsageSummary AdjustGauge(IDbConnection db, Workspace workspace, BillingSubscription subscription,
        string userId, string meterKey, long delta, string idempotencyKey, string source)
    {
        var duplicate = db.Single<UsageEvent>(x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey);
        var usage = GetUsage(db, workspace, subscription).FirstOrDefault(x => x.MeterKey == meterKey)
            ?? throw new HttpError(404, "MeterNotFound", $"Meter '{meterKey}' is not defined by the effective plan.");
        if (usage.Kind != MeterKind.Gauge)
            throw new HttpError(409, "GaugeRequired", $"Meter '{meterKey}' is not a gauge.");
        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == workspace.Id && x.MeterKey == meterKey && x.PeriodStart == usage.PeriodStart);
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        if (duplicate != null) return ToSummary(period, aggregate, usage.DisplayName);
        var next = aggregate.UsedUnits + delta;
        if (next < 0) throw new HttpError(409, "GaugeUnderflow", "The usage adjustment would make the gauge negative.");
        if (delta > 0 && usage.Allowance != null && usage.Enforcement == QuotaEnforcement.HardLimit && next + aggregate.ReservedUnits > usage.Allowance)
            throw new HttpError(429, "QuotaExceeded", $"This operation would exceed the {usage.DisplayName} allowance.");
        var now = DateTime.UtcNow;
        using var tx = db.OpenTransaction();
        db.Insert(new UsageEvent {
            WorkspaceId = workspace.Id, UsagePeriodId = period.Id, MeterKey = meterKey, Units = delta,
            IdempotencyKey = idempotencyKey, Source = source, EventType = "gauge-adjustment", RecordedDate = now, RecordedBy = userId,
        });
        var updated = db.ExecuteSql(@"UPDATE UsageAggregate
SET UsedUnits = UsedUnits + @Delta,
    PeakUnits = CASE WHEN PeakUnits > UsedUnits + @Delta THEN PeakUnits ELSE UsedUnits + @Delta END,
    LastEventDate = @Now, ModifiedDate = @Now, ModifiedBy = @UserId
WHERE Id = @Id
  AND UsedUnits + @Delta >= 0
  AND (@HardLimit = 0 OR @Allowance IS NULL OR UsedUnits + ReservedUnits + @Delta <= @Allowance)",
            new { Delta = delta, Now = now, UserId = userId, aggregate.Id,
                HardLimit = usage.Enforcement == QuotaEnforcement.HardLimit && delta > 0 ? 1 : 0, usage.Allowance });
        if (updated != 1)
            throw new HttpError(delta < 0 ? 409 : 429, delta < 0 ? "GaugeUnderflow" : "QuotaExceeded",
                delta < 0 ? "The usage adjustment would make the gauge negative." : $"This operation would exceed the {usage.DisplayName} allowance.");
        aggregate = db.SingleById<UsageAggregate>(aggregate.Id);
        tx.Commit();
        return ToSummary(period, aggregate, usage.DisplayName);
    }

    public SaasPlanDetails GetPlanDetails(IDbConnection db, string planId)
    {
        var plan = db.SingleById<SaasPlan>(planId)
            ?? throw new HttpError(404, "PlanNotFound", "The selected plan was not found.");
        var versions = db.Select<SaasPlanVersion>(x => x.PlanId == planId).OrderByDescending(x => x.Version).ToList();
        var draft = versions.FirstOrDefault(x => x.Status == PlanVersionStatus.Draft);
        var version = draft ?? versions.FirstOrDefault(x => x.Status == PlanVersionStatus.Published)
            ?? versions.FirstOrDefault()
            ?? throw new HttpError(404, "PlanVersionNotFound", "The selected plan does not have a version.");
        plan.Name = version.Name ?? plan.Name;
        plan.Description = version.Description ?? plan.Description;
        plan.DisplayOrder = version.DisplayOrder ?? plan.DisplayOrder;
        plan.IsPublic = version.IsPublic ?? plan.IsPublic;
        plan.IsContactSales = version.IsContactSales ?? plan.IsContactSales;
        plan.IsArchived = version.IsArchived ?? plan.IsArchived;
        var versionIds = new HashSet<string>(versions.Select(x => x.Id));
        var activeSubscriptions = db.Select<BillingSubscription>(x => x.Status != SubscriptionStatus.Canceled)
            .LongCount(x => versionIds.Contains(x.PlanVersionId));

        return new SaasPlanDetails {
            Plan = plan,
            Version = version,
            HasDraft = draft != null,
            ActiveSubscriptions = activeSubscriptions,
            Prices = db.Select<SaasPlanPrice>(x => x.PlanVersionId == version.Id)
                .OrderBy(x => x.Currency).ThenBy(x => x.Interval).ToList(),
            Features = db.Select<SaasPlanFeature>(x => x.PlanVersionId == version.Id)
                .OrderBy(x => x.DisplayOrder).ToList(),
            Quotas = db.Select<SaasPlanQuota>(x => x.PlanVersionId == version.Id)
                .OrderBy(x => x.DisplayName).ToList(),
        };
    }

    public SaasPlanDetails SavePlanDraft(IDbConnection db, string actorId, SaveSaasPlanDraft request)
    {
        ValidatePlanDraft(request);
        var plan = db.SingleById<SaasPlan>(request.PlanId)
            ?? throw new HttpError(404, "PlanNotFound", "The selected plan was not found.");
        var versions = db.Select<SaasPlanVersion>(x => x.PlanId == plan.Id).OrderByDescending(x => x.Version).ToList();
        var draft = versions.FirstOrDefault(x => x.Status == PlanVersionStatus.Draft);
        var now = DateTime.UtcNow;

        using (var tx = db.OpenTransaction())
        {
        if (draft == null)
        {
            draft = new SaasPlanVersion {
                PlanId = plan.Id,
                Version = versions.Count == 0 ? 1 : versions.Max(x => x.Version) + 1,
                Status = PlanVersionStatus.Draft,
                CreatedDate = now,
                CreatedBy = actorId,
            };
            db.Insert(draft);
        }
        draft.Name = request.Name.Trim();
        draft.Description = request.Description.Trim();
        draft.DisplayOrder = request.DisplayOrder;
        draft.IsPublic = request.IsPublic;
        draft.IsContactSales = request.IsContactSales;
        draft.IsArchived = request.IsArchived;
        draft.TrialDays = request.TrialDays is > 0 ? request.TrialDays : null;
        draft.EffectiveFrom = null;
        draft.PublishedDate = null;
        draft.PublishedBy = null;
        draft.ModifiedDate = now;
        draft.ModifiedBy = actorId;
        db.Update(draft);

        db.Delete<SaasPlanPrice>(x => x.PlanVersionId == draft.Id);
        db.Delete<SaasPlanFeature>(x => x.PlanVersionId == draft.Id);
        db.Delete<SaasPlanQuota>(x => x.PlanVersionId == draft.Id);

        foreach (var price in request.Prices)
            db.Insert(new SaasPlanPrice {
                PlanVersionId = draft.Id,
                Currency = price.Currency.Trim().ToLowerInvariant(),
                Interval = price.Interval,
                UnitAmount = price.UnitAmount,
                StripePriceId = NormalizeOptional(price.StripePriceId),
                IsActive = price.IsActive,
                CreatedDate = now, ModifiedDate = now, CreatedBy = actorId, ModifiedBy = actorId,
            });
        foreach (var feature in request.Features)
            db.Insert(new SaasPlanFeature {
                PlanVersionId = draft.Id,
                Key = feature.Key.Trim().ToLowerInvariant(),
                Name = feature.Name.Trim(),
                Description = NormalizeOptional(feature.Description),
                Enabled = feature.Enabled,
                DisplayOrder = feature.DisplayOrder,
                CreatedDate = now, ModifiedDate = now, CreatedBy = actorId, ModifiedBy = actorId,
            });
        foreach (var quota in request.Quotas)
            db.Insert(new SaasPlanQuota {
                PlanVersionId = draft.Id,
                MeterKey = quota.MeterKey.Trim().ToLowerInvariant(),
                DisplayName = quota.DisplayName.Trim(),
                IncludedUnits = quota.IncludedUnits,
                Enforcement = quota.Enforcement,
                RolloverEnabled = quota.RolloverEnabled,
                CreatedDate = now, ModifiedDate = now, CreatedBy = actorId, ModifiedBy = actorId,
            });
        db.Insert(new SaasAuditEvent {
            Category = "plan", Action = "draft.saved", ActorId = actorId, SubjectId = draft.Id,
            DetailJson = new { plan.Id, plan.Code, draft.Version }.ToJson(), CreatedDate = now,
        });
        tx.Commit();
        }
        return GetPlanDetails(db, plan.Id);
    }

    public SaasPlanDetails PublishPlanDraft(IDbConnection db, string actorId, string planId)
    {
        var plan = db.SingleById<SaasPlan>(planId)
            ?? throw new HttpError(404, "PlanNotFound", "The selected plan was not found.");
        var draft = db.Select<SaasPlanVersion>(x => x.PlanId == planId && x.Status == PlanVersionStatus.Draft)
            .OrderByDescending(x => x.Version).FirstOrDefault()
            ?? throw new HttpError(409, "PlanDraftNotFound", "Save a draft before publishing this plan.");
        var prices = db.Select<SaasPlanPrice>(x => x.PlanVersionId == draft.Id);
        var features = db.Select<SaasPlanFeature>(x => x.PlanVersionId == draft.Id);
        var quotas = db.Select<SaasPlanQuota>(x => x.PlanVersionId == draft.Id);
        ValidatePublishablePlan(draft.IsContactSales ?? plan.IsContactSales, draft, prices, features, quotas);
        var now = DateTime.UtcNow;

        using (var tx = db.OpenTransaction())
        {
        plan.Name = draft.Name ?? plan.Name;
        plan.Description = draft.Description ?? plan.Description;
        plan.DisplayOrder = draft.DisplayOrder ?? plan.DisplayOrder;
        plan.IsPublic = draft.IsPublic ?? plan.IsPublic;
        plan.IsContactSales = draft.IsContactSales ?? plan.IsContactSales;
        plan.IsArchived = draft.IsArchived ?? plan.IsArchived;
        plan.ModifiedDate = now;
        plan.ModifiedBy = actorId;
        db.Update(plan);
        foreach (var published in db.Select<SaasPlanVersion>(x => x.PlanId == planId && x.Status == PlanVersionStatus.Published))
        {
            published.Status = PlanVersionStatus.Retired;
            published.ModifiedDate = now;
            published.ModifiedBy = actorId;
            db.Update(published);
        }
        draft.Status = PlanVersionStatus.Published;
        draft.EffectiveFrom = now;
        draft.PublishedDate = now;
        draft.PublishedBy = actorId;
        draft.ModifiedDate = now;
        draft.ModifiedBy = actorId;
        db.Update(draft);
        db.Insert(new SaasAuditEvent {
            Category = "plan", Action = "version.published", ActorId = actorId, SubjectId = draft.Id,
            DetailJson = new { plan.Id, plan.Code, draft.Version }.ToJson(), CreatedDate = now,
        });
        tx.Commit();
        }
        return GetPlanDetails(db, plan.Id);
    }

    private void ValidatePlanDraft(SaveSaasPlanDraft request)
    {
        request.Prices ??= [];
        request.Features ??= [];
        request.Quotas ??= [];
        if (request.Name.IsNullOrEmpty() || request.Description.IsNullOrEmpty())
            throw new HttpError(400, "PlanDetailsRequired", "Plan name and description are required.");
        if (request.DisplayOrder < 0)
            throw new HttpError(400, "InvalidDisplayOrder", "Display order cannot be negative.");
        if (request.TrialDays != null && request.TrialDays is < 1 or > 365)
            throw new HttpError(400, "InvalidTrialDays", "An enabled trial must be between 1 and 365 days.");
        if (!config.EnableTrials && request.TrialDays != null)
            throw new HttpError(409, "TrialsDisabled", "Trials are disabled by the deployment-wide SaaS configuration.");
        if (request.Prices.Any(x => x.UnitAmount < 0 || x.Currency.IsNullOrEmpty() || x.Currency.Trim().Length != 3 || !x.Currency.Trim().All(char.IsLetter)))
            throw new HttpError(400, "InvalidPrice", "Prices need a three-letter currency and a non-negative amount.");
        if (request.Prices.GroupBy(x => $"{x.Currency.Trim().ToLowerInvariant()}:{x.Interval}").Any(x => x.Count() > 1))
            throw new HttpError(409, "DuplicatePrice", "Only one price is allowed for each currency and billing interval.");
        if (request.Features.Any(x => x.Key.IsNullOrEmpty() || x.Name.IsNullOrEmpty()) ||
            request.Features.GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new HttpError(409, "DuplicateFeature", "Feature keys must be present and unique.");
        var featureKeys = config.Features.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownFeature = request.Features.FirstOrDefault(x => !featureKeys.Contains(x.Key.Trim()));
        if (unknownFeature != null)
            throw new HttpError(400, "UnknownFeature", $"Feature '{unknownFeature.Key}' is not registered in SaaS configuration.");
        if (request.Quotas.Any(x => x.MeterKey.IsNullOrEmpty() || x.DisplayName.IsNullOrEmpty() || x.IncludedUnits < 0) ||
            request.Quotas.GroupBy(x => x.MeterKey.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new HttpError(409, "DuplicateQuota", "Meter keys must be present and unique, and allowances cannot be negative.");
        var meterKeys = config.Meters.Select(x => x.Key).Append("workspace.seats").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownMeter = request.Quotas.FirstOrDefault(x => !meterKeys.Contains(x.MeterKey.Trim()));
        if (unknownMeter != null)
            throw new HttpError(400, "UnknownMeter", $"Meter '{unknownMeter.MeterKey}' is not registered in SaaS configuration.");
    }

    private static void ValidatePublishablePlan(bool isContactSales, SaasPlanVersion version,
        List<SaasPlanPrice> prices, List<SaasPlanFeature> features, List<SaasPlanQuota> quotas)
    {
        if (!isContactSales && !prices.Any(x => x.Interval == BillingInterval.Month && x.IsActive))
            throw new HttpError(409, "MonthlyPriceRequired", "A self-serve plan needs an active monthly price before it can be published.");
        if (features.Count == 0)
            throw new HttpError(409, "PlanFeatureRequired", "Add at least one customer-facing feature before publishing.");
        if (quotas.Count == 0)
            throw new HttpError(409, "PlanQuotaRequired", "Add at least one usage quota before publishing.");
        if (version.TrialDays != null && version.TrialDays is < 1 or > 365)
            throw new HttpError(400, "InvalidTrialDays", "An enabled trial must be between 1 and 365 days.");
    }

    private static string? NormalizeOptional(string? value) => value.IsNullOrEmpty() ? null : value!.Trim();

    private UsagePeriod EnsureUsagePeriod(IDbConnection db, string workspaceId, BillingSubscription subscription,
        PlanQuotaInfo quota, long? allowance, string source)
    {
        var meter = config.Meters.LastOrDefault(x => x.Key.Equals(quota.MeterKey, StringComparison.OrdinalIgnoreCase));
        var kind = quota.MeterKey == "workspace.seats" ? MeterKind.Gauge : meter?.Kind ?? MeterKind.Counter;
        var reset = quota.MeterKey == "workspace.seats" ? MeterReset.Never : meter?.Reset ?? MeterReset.BillingPeriod;
        var now = DateTime.UtcNow;
        var periodStart = reset switch {
            MeterReset.Never => DateTime.UnixEpoch,
            MeterReset.CalendarMonth => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => subscription.PeriodStart,
        };
        var periodEnd = reset switch {
            MeterReset.Never => DateTime.MaxValue,
            MeterReset.CalendarMonth => periodStart.AddMonths(1),
            _ => subscription.PeriodEnd,
        };
        if (quota.RolloverEnabled && allowance != null)
        {
            var previous = db.Select<UsagePeriod>(x => x.WorkspaceId == workspaceId && x.MeterKey == quota.MeterKey
                    && x.PeriodStart < periodStart)
                .OrderByDescending(x => x.PeriodStart).FirstOrDefault();
            if (previous != null)
            {
                var previousUsage = db.Single<UsageAggregate>(x => x.UsagePeriodId == previous.Id);
                allowance += Math.Max(0, (previous.Allowance ?? 0) - previousUsage.UsedUnits - previousUsage.ReservedUnits);
                source += "+rollover";
            }
        }
        var existing = db.Single<UsagePeriod>(x => x.WorkspaceId == workspaceId && x.MeterKey == quota.MeterKey
            && x.PeriodStart == periodStart);
        if (existing != null)
        {
            if (existing.Allowance != allowance || existing.Enforcement != quota.Enforcement || existing.Source != source ||
                existing.Kind != kind || existing.Reset != reset || existing.PeriodEnd != periodEnd)
            {
                existing.Allowance = allowance;
                existing.Enforcement = quota.Enforcement;
                existing.Source = source;
                existing.Kind = kind;
                existing.Reset = reset;
                existing.PeriodEnd = periodEnd;
                existing.ModifiedDate = DateTime.UtcNow;
                existing.ModifiedBy = "entitlement-resolution";
                db.Update(existing);
            }
            return existing;
        }
        var period = new UsagePeriod {
            WorkspaceId = workspaceId, MeterKey = quota.MeterKey, PeriodStart = periodStart,
            PeriodEnd = periodEnd, Allowance = allowance, Enforcement = quota.Enforcement, Source = source,
            Kind = kind, Reset = reset,
            CreatedDate = now, ModifiedDate = now,
        };
        db.Insert(period);
        db.Insert(new UsageAggregate {
            UsagePeriodId = period.Id, CreatedDate = now, ModifiedDate = now,
        });
        return period;
    }

    private static void EnsureCurrentFreePeriod(IDbConnection db, BillingSubscription subscription)
    {
        if (subscription.Status != SubscriptionStatus.Free || subscription.PeriodEnd > DateTime.UtcNow) return;
        var now = DateTime.UtcNow;
        subscription.PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.PeriodEnd = subscription.PeriodStart.AddMonths(1);
        subscription.ModifiedDate = now;
        db.Update(subscription);
    }

    private void ApplyLifecyclePolicy(IDbConnection db, Workspace workspace, BillingSubscription subscription)
    {
        var now = DateTime.UtcNow;
        var previous = subscription.AccessMode;
        var mode = subscription.Status switch {
            SubscriptionStatus.PastDue when subscription.GraceEnd == null || subscription.GraceEnd > now => WorkspaceAccessMode.Grace,
            SubscriptionStatus.PastDue => ParseAccessMode(config.AfterGraceAccessMode, WorkspaceAccessMode.FreeFallback),
            SubscriptionStatus.Paused or SubscriptionStatus.Canceled => ParseAccessMode(config.AfterGraceAccessMode, WorkspaceAccessMode.FreeFallback),
            SubscriptionStatus.Trialing when subscription.TrialEnd != null && subscription.TrialEnd <= now => WorkspaceAccessMode.FreeFallback,
            _ => WorkspaceAccessMode.Full,
        };
        if (workspace.Status == WorkspaceStatus.Suspended) mode = WorkspaceAccessMode.Suspended;
        if (workspace.Status == WorkspaceStatus.PendingDeletion) mode = WorkspaceAccessMode.ReadOnly;
        if (previous == mode) return;
        subscription.AccessMode = mode;
        subscription.AccessReason = mode switch {
            WorkspaceAccessMode.Grace => "Payment is past due; access continues during the configured grace period.",
            WorkspaceAccessMode.ReadOnly => "The organization is read-only.",
            WorkspaceAccessMode.FreeFallback => "Paid access is unavailable; Free plan entitlements apply.",
            WorkspaceAccessMode.Suspended => "The organization is suspended by an operator.",
            _ => null,
        };
        subscription.ModifiedDate = now;
        subscription.ModifiedBy = "lifecycle-policy";
        db.Update(subscription);
        db.Insert(new SaasAuditEvent {
            WorkspaceId=workspace.Id, Category="billing", Action="access-mode.changed", ActorId="lifecycle-policy",
            SubjectId=subscription.Id, DetailJson=new { previous, current=mode, subscription.Status }.ToJson(), CreatedDate=now,
        });
    }

    private string GetEffectivePlanVersionId(IDbConnection db, BillingSubscription subscription)
    {
        if (subscription.AccessMode != WorkspaceAccessMode.FreeFallback) return subscription.PlanVersionId;
        var freePlan = db.Single<SaasPlan>(x => x.Code == config.DefaultPlan && !x.IsArchived)
            ?? throw new HttpError(404, "DefaultPlanNotFound", "The configured fallback plan was not found.");
        return db.Select<SaasPlanVersion>(x => x.PlanId == freePlan.Id && x.Status == PlanVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefault()?.Id
            ?? throw new HttpError(404, "DefaultPlanVersionNotFound", "The configured fallback plan has no published version.");
    }

    private static WorkspaceAccessMode ParseAccessMode(string value, WorkspaceAccessMode fallback) =>
        Enum.TryParse<WorkspaceAccessMode>(value, true, out var parsed) ? parsed : fallback;

    private static UsageSummary ToSummary(UsagePeriod period, UsageAggregate aggregate, string displayName)
    {
        var remaining = period.Allowance == null ? long.MaxValue : Math.Max(0, period.Allowance.Value - aggregate.UsedUnits - aggregate.ReservedUnits);
        return new UsageSummary {
            MeterKey = period.MeterKey, DisplayName = displayName,
            UsedUnits = aggregate.UsedUnits, ReservedUnits = aggregate.ReservedUnits, PeakUnits = aggregate.PeakUnits,
            Allowance = period.Allowance, RemainingUnits = remaining,
            PercentUsed = period.Allowance is > 0 ? Math.Round(aggregate.UsedUnits * 100d / period.Allowance.Value, 2) : 0,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Enforcement = period.Enforcement,
            Kind = period.Kind, Reset = period.Reset, Source = period.Source,
        };
    }

    private static HttpError QuotaExceeded(IDbConnection db, Workspace workspace, string actorId,
        UsageSummary usage, long requestedUnits, string operation)
    {
        db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "usage", Action = "quota.rejected", Outcome = "Rejected",
            ActorId = actorId, SubjectId = usage.MeterKey, Reason = $"Hard limit rejected {operation}.",
            DetailJson = new { usage.MeterKey, requestedUnits, usage.UsedUnits, usage.ReservedUnits, usage.Allowance }.ToJson(),
            CreatedDate = DateTime.UtcNow,
        });
        SaasTelemetry.QuotaRejected.Add(1, new KeyValuePair<string, object?>("meter", usage.MeterKey));
        return new HttpError(429, "QuotaExceeded", $"This operation would exceed the {usage.DisplayName} allowance.");
    }

    private static string Slugify(string value)
    {
        var slug = new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        slug = slug.Length > 48 ? slug[..48] : slug;
        return string.IsNullOrEmpty(slug) ? "workspace" : slug;
    }
}

public class SaasServices(
    SaasConfig config,
    ProductConfig product,
    NotificationConfig notificationConfig,
    ISaasManager manager,
    IWorkspaceContextResolver workspaceContexts,
    IEntitlementResolver entitlementResolver,
    IStripeBillingGateway stripe,
    IBackgroundJobs jobs) : Service
{
    public object Any(GetSaasPlans request)
    {
        var results = Db.Select<SaasPlan>(x => x.IsPublic && !x.IsArchived).OrderBy(x => x.DisplayOrder)
            .Select(plan => {
                var version = Db.Select<SaasPlanVersion>(x => x.PlanId == plan.Id && x.Status == PlanVersionStatus.Published)
                    .OrderByDescending(x => x.Version).First();
                return manager.GetPlanInfo(Db, version.Id);
            }).ToList();
        return new GetSaasPlansResponse { Results = results, DefaultCurrency = config.DefaultCurrency, AnnualBillingEnabled = config.EnableAnnualBilling };
    }

    public async Task<object> Any(GetSaasDashboard request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var workspace = context.Workspace;
        var member = context.Member;
        var subscription = Db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        var usage = manager.GetUsage(Db, workspace, subscription);
        var planVersionId = subscription.PlanVersionId;
        if (subscription.AccessMode == WorkspaceAccessMode.FreeFallback)
        {
            var freePlan = Db.Single<SaasPlan>(x => x.Code == config.DefaultPlan && !x.IsArchived);
            planVersionId = Db.Select<SaasPlanVersion>(x => x.PlanId == freePlan.Id && x.Status == PlanVersionStatus.Published)
                .OrderByDescending(x => x.Version).First().Id;
        }
        return new GetSaasDashboardResponse {
            Workspace = workspace, MemberRole = member.Role.ToString(), Subscription = subscription,
            Plan = manager.GetPlanInfo(Db, planVersionId), Usage = usage,
            Entitlements = entitlementResolver.GetEffective(Db, workspace, subscription),
            UnreadNotifications = Db.Count<NotificationDelivery>(x => x.WorkspaceId == workspace.Id && x.UserId == session.UserAuthId && x.Channel == NotificationChannel.InApp && x.ReadDate == null),
        };
    }

    public async Task<object> Any(GetWorkspaceApiKeys request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var now = DateTime.UtcNow;
        var results = Db.Select<ApiKeysFeature.ApiKey>(x => x.UserId == session.UserAuthId && x.RefIdStr == context.Workspace.Id)
            .OrderByDescending(x => x.Id)
            .Select(x => new WorkspaceApiKeyInfo {
                Id = x.Id,
                Name = x.Name ?? "Unnamed key",
                VisibleKey = x.VisibleKey ?? "",
                CreatedDate = x.CreatedDate,
                ExpiryDate = x.ExpiryDate,
                LastUsedDate = x.LastUsedDate,
                Active = x.CancelledDate == null && (x.ExpiryDate == null || x.ExpiryDate > now),
            }).ToList();
        return new GetWorkspaceApiKeysResponse { Results = results };
    }

    public async Task<object> Any(GetMyWorkspaces request)
    {
        AssertInteractiveRequest();
        var session = await GetSessionAsync();
        var active = manager.EnsurePersonalWorkspace(Db, session.UserAuthId!, session.DisplayName, session.Email);
        var memberships = Db.Select<WorkspaceMember>(x => x.UserId == session.UserAuthId && x.Status == WorkspaceMemberStatus.Active);
        var workspaces = Db.SelectByIds<Workspace>(memberships.Select(x => x.WorkspaceId)).ToDictionary(x => x.Id);
        return new GetMyWorkspacesResponse {
            Results = memberships.Where(x => workspaces.ContainsKey(x.WorkspaceId))
                .OrderBy(x => workspaces[x.WorkspaceId].Name)
                .Select(x => new WorkspaceAccessInfo { Workspace = workspaces[x.WorkspaceId], Role = x.Role, IsActive = x.WorkspaceId == active.Id })
                .ToList(),
        };
    }

    public async Task<object> Any(CreateOrganization request)
    {
        AssertInteractiveRequest();
        var session = await GetSessionAsync();
        var workspace = manager.CreateOrganization(Db, session.UserAuthId!, request.Name, request.BillingEmail ?? session.Email);
        return new WorkspaceAccessInfo { Workspace = workspace, Role = WorkspaceMemberRole.Owner, IsActive = true };
    }

    public async Task<object> Any(SwitchWorkspace request)
    {
        AssertInteractiveRequest();
        var session = await GetSessionAsync();
        manager.EnsurePersonalWorkspace(Db, session.UserAuthId!, session.DisplayName, session.Email);
        var membership = Db.Single<WorkspaceMember>(x => x.WorkspaceId == request.WorkspaceId &&
            x.UserId == session.UserAuthId && x.Status == WorkspaceMemberStatus.Active)
            ?? throw new HttpError(403, "WorkspaceAccessDenied", "You do not have access to this organization.");
        var now = DateTime.UtcNow;
        var preference = Db.SingleById<UserWorkspacePreference>(session.UserAuthId!)
            ?? new UserWorkspacePreference { UserId = session.UserAuthId!, CreatedDate = now, CreatedBy = session.UserAuthId! };
        preference.ActiveWorkspaceId = membership.WorkspaceId;
        preference.ModifiedDate = now;
        preference.ModifiedBy = session.UserAuthId!;
        Db.Save(preference);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = membership.WorkspaceId, Category = "workspace", Action = "workspace.selected",
            ActorId = session.UserAuthId!, SubjectId = membership.WorkspaceId, CreatedDate = now,
        });
        return new EmptyResponse();
    }

    public async Task<object> Any(RecordUsage request)
    {
        var session = await GetSessionAsync();
        var workspace = workspaceContexts.Resolve(Db, session, Request).Workspace;
        var subscription = Db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        if (!entitlementResolver.HasFeature(Db, workspace, subscription, "api.access"))
            throw new HttpError(403, "FeatureNotEntitled", "API access is not included in the current plan.");
        var result = manager.RecordUsage(Db, workspace, subscription, session.UserAuthId!, request);
        if (result.Usage != null)
        {
            Response?.AddHeader("X-Quota-Meter", result.Usage.MeterKey);
            Response?.AddHeader("X-Quota-Used", result.Usage.UsedUnits.ToString());
            Response?.AddHeader("X-Quota-Remaining", result.Usage.RemainingUnits.ToString());
            if (result.Usage.Allowance != null)
                Response?.AddHeader("X-Quota-Limit", result.Usage.Allowance.Value.ToString());
            Response?.AddHeader("X-Quota-Reset", result.Usage.PeriodEnd.ToUniversalTime().ToString("O"));
        }
        return result;
    }

    public async Task<object> Any(UpdateWorkspaceProfile request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var workspace = context.Workspace;
        AssertWorkspaceAdmin(context);
        var normalizedSlug = new string(request.Slug.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (Db.Exists<Workspace>(x => x.Slug == normalizedSlug && x.Id != workspace.Id))
            throw new HttpError(409, "SlugAlreadyExists", "That organization address is already in use.");
        workspace.Name = request.Name.Trim();
        workspace.Slug = normalizedSlug;
        workspace.BillingEmail = request.BillingEmail?.Trim();
        workspace.ModifiedDate = DateTime.UtcNow;
        workspace.ModifiedBy = session.UserAuthId!;
        Db.Update(workspace);
        Db.Insert(new SaasAuditEvent { WorkspaceId = workspace.Id, Category = "workspace", Action = "profile.updated", ActorId = session.UserAuthId!, SubjectId = workspace.Id, CreatedDate = DateTime.UtcNow });
        return workspace;
    }

    public async Task<object> Any(GetWorkspaceMembers request)
    {
        var session = await GetSessionAsync();
        var workspace = workspaceContexts.Resolve(Db, session, Request).Workspace;
        var members = Db.Select<WorkspaceMember>(x => x.WorkspaceId == workspace.Id);
        var users = Db.SelectByIds<User>(members.Where(x => !x.UserId.StartsWith("invite:"))
                .Select(x => x.UserId))
            .ToDictionary(x => x.Id);
        return new GetWorkspaceMembersResponse { Results = members.Select(x => {
            users.TryGetValue(x.UserId, out var user);
            return new WorkspaceMemberInfo {
                Id = x.Id,
                UserId = x.UserId,
                DisplayName = user?.DisplayName,
                Email = x.InvitedEmail ?? user?.Email ?? user?.UserName,
                Role = x.Role,
                Status = x.Status,
                InvitationEmailSent = x.InvitationSentDate != null,
                InvitedDate = x.InvitedDate,
                InvitationExpiresAt = x.InvitationExpiresAt,
                InvitationExpired = x.Status == WorkspaceMemberStatus.Invited && x.InvitationExpiresAt <= DateTime.UtcNow,
                JoinedDate = x.JoinedDate,
            };
        }).ToList() };
    }

    public async Task<object> Any(InviteWorkspaceMember request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var workspace = context.Workspace;
        AssertWorkspaceAdmin(context);
        if (request.Role == WorkspaceMemberRole.Owner)
            throw new HttpError(409, "OwnershipTransferRequired", "Invite the member first, then use the ownership transfer workflow.");
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = Db.Single<WorkspaceMember>(x => x.WorkspaceId == workspace.Id && x.InvitedEmail == email);
        if (existing != null && existing.Status != WorkspaceMemberStatus.Disabled)
            throw new HttpError(409, "MemberAlreadyInvited", "This email already belongs to the organization.");
        var subscription = Db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        var seats = manager.GetUsage(Db, workspace, subscription).FirstOrDefault(x => x.MeterKey == "workspace.seats")?.Allowance;
        var now = DateTime.UtcNow;
        var memberCount = Db.Count<WorkspaceMember>(x => x.WorkspaceId == workspace.Id &&
            (x.Status == WorkspaceMemberStatus.Active ||
             (x.Status == WorkspaceMemberStatus.Invited && (x.InvitationExpiresAt == null || x.InvitationExpiresAt > now))));
        if (seats != null && memberCount >= seats)
            throw new HttpError(429, "SeatQuotaExceeded", "Upgrade the organization plan before inviting another member.");
        var sendEmail = notificationConfig.Provider == EmailProvider.Smtp && TryResolve<SmtpConfig>() != null;
        var token = WorkspaceInvitationTokens.Create();
        var member = existing ?? new WorkspaceMember {
            WorkspaceId=workspace.Id, UserId=$"invite:{email}", InvitedEmail=email,
            CreatedDate=now, CreatedBy=session.UserAuthId!,
        };
        member.Role = request.Role;
        member.Status = WorkspaceMemberStatus.Invited;
        member.InvitedDate = now;
        member.InvitationSentDate = sendEmail ? now : null;
        member.InvitationTokenHash = WorkspaceInvitationTokens.Hash(token);
        member.InvitationExpiresAt = now.AddDays(Math.Max(1, config.InvitationExpiryDays));
        member.InvitationAcceptedDate = null;
        member.InvitationRevokedDate = null;
        member.JoinedDate = null;
        member.ModifiedDate = now;
        member.ModifiedBy = session.UserAuthId!;
        Db.Save(member);
        Db.Insert(new SaasAuditEvent { WorkspaceId=workspace.Id, Category="membership", Action="member.invited", ActorId=session.UserAuthId!, SubjectId=member.Id, DetailJson=new { email, role=request.Role }.ToJson(), CreatedDate=now });
        var invitationUrl = BuildInvitationUrl(token);
        if (sendEmail)
            QueueInvitationEmail(workspace, member, invitationUrl);
        return ToMemberInfo(member, invitationUrl);
    }

    public async Task<object> Any(ResendWorkspaceInvitation request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        AssertWorkspaceAdmin(context);
        var member = Db.Single<WorkspaceMember>(x => x.Id == request.Id && x.WorkspaceId == context.Workspace.Id)
            ?? throw new HttpError(404, "InvitationNotFound", "The organization invitation was not found.");
        if (member.Status != WorkspaceMemberStatus.Invited || member.InvitedEmail.IsNullOrEmpty())
            throw new HttpError(409, "InvitationNotPending", "Only pending invitations can be resent.");

        var now = DateTime.UtcNow;
        var token = WorkspaceInvitationTokens.Create();
        var sendEmail = notificationConfig.Provider == EmailProvider.Smtp && TryResolve<SmtpConfig>() != null;
        member.InvitedDate = now;
        member.InvitationSentDate = sendEmail ? now : null;
        member.InvitationTokenHash = WorkspaceInvitationTokens.Hash(token);
        member.InvitationExpiresAt = now.AddDays(Math.Max(1, config.InvitationExpiryDays));
        member.ModifiedDate = now;
        member.ModifiedBy = session.UserAuthId!;
        Db.Update(member);
        var invitationUrl = BuildInvitationUrl(token);
        if (sendEmail)
            QueueInvitationEmail(context.Workspace, member, invitationUrl);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId=context.Workspace.Id, Category="membership", Action="invitation.resent",
            ActorId=session.UserAuthId!, SubjectId=member.Id, CreatedDate=now,
        });
        return ToMemberInfo(member, invitationUrl);
    }

    public async Task<object> Any(AcceptWorkspaceInvitation request)
    {
        AssertInteractiveRequest();
        var session = await GetSessionAsync();
        var tokenHash = WorkspaceInvitationTokens.Hash(request.Token.Trim());
        var member = Db.Single<WorkspaceMember>(x => x.InvitationTokenHash == tokenHash)
            ?? throw new HttpError(404, "InvitationNotFound", "This invitation is invalid or has already been used.");
        var now = DateTime.UtcNow;
        var sessionEmail = (session.Email ?? session.UserName)?.Trim().ToLowerInvariant();
        WorkspaceInvitationTokens.AssertCanAccept(member, sessionEmail, now);

        var existing = Db.Single<WorkspaceMember>(x => x.WorkspaceId == member.WorkspaceId && x.UserId == session.UserAuthId);
        if (existing != null && existing.Id != member.Id)
        {
            if (existing.Status != WorkspaceMemberStatus.Active)
            {
                existing.Status = WorkspaceMemberStatus.Active;
                existing.JoinedDate = now;
                existing.ModifiedDate = now;
                existing.ModifiedBy = session.UserAuthId!;
                Db.Update(existing);
            }
            Db.DeleteById<WorkspaceMember>(member.Id);
            member = existing;
        }
        else
        {
            member.UserId = session.UserAuthId!;
            member.Status = WorkspaceMemberStatus.Active;
            member.InvitationAcceptedDate = now;
            member.InvitationTokenHash = null;
            member.JoinedDate = now;
            member.ModifiedDate = now;
            member.ModifiedBy = session.UserAuthId!;
            Db.Update(member);
        }
        SaveActiveWorkspacePreference(session.UserAuthId!, member.WorkspaceId, now);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId=member.WorkspaceId, Category="membership", Action="invitation.accepted",
            ActorId=session.UserAuthId!, SubjectId=member.Id, CreatedDate=now,
        });
        var workspace = Db.SingleById<Workspace>(member.WorkspaceId)
            ?? throw new HttpError(404, "WorkspaceNotFound", "The invited organization was not found.");
        return new WorkspaceAccessInfo { Workspace=workspace, Role=member.Role, IsActive=true };
    }

    public async Task<object> Any(UpdateWorkspaceMemberRole request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var workspace = context.Workspace;
        AssertWorkspaceAdmin(context);
        var member = Db.Single<WorkspaceMember>(x => x.Id == request.Id && x.WorkspaceId == workspace.Id)
            ?? throw new HttpError(404, "WorkspaceMemberNotFound", "The organization member was not found.");
        if (member.Role == WorkspaceMemberRole.Owner || request.Role == WorkspaceMemberRole.Owner)
            throw new HttpError(409, "OwnershipTransferRequired", "Use the ownership transfer workflow to change the Owner role.");
        var previous = member.Role;
        member.Role = request.Role;
        member.ModifiedDate = DateTime.UtcNow;
        member.ModifiedBy = session.UserAuthId!;
        Db.Update(member);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId=workspace.Id, Category="membership", Action="member.role-changed", ActorId=session.UserAuthId!,
            SubjectId=member.Id, DetailJson=new { previous, current=request.Role }.ToJson(), CreatedDate=DateTime.UtcNow,
        });
        return new WorkspaceMemberInfo { Id=member.Id, UserId=member.UserId, Email=member.InvitedEmail, Role=member.Role, Status=member.Status, JoinedDate=member.JoinedDate };
    }

    public async Task<object> Any(RemoveWorkspaceMember request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        var workspace = context.Workspace;
        AssertWorkspaceAdmin(context);
        var member = Db.Single<WorkspaceMember>(x => x.Id == request.Id && x.WorkspaceId == workspace.Id)
            ?? throw new HttpError(404, "WorkspaceMemberNotFound", "The organization member was not found.");
        if (member.Role == WorkspaceMemberRole.Owner)
            throw new HttpError(409, "OwnershipTransferRequired", "Transfer ownership before removing the organization Owner.");
        if (member.UserId == session.UserAuthId)
            throw new HttpError(409, "LeaveWorkspaceRequired", "Use the leave-workspace workflow to remove your own membership.");
        var now = DateTime.UtcNow;
        var wasInvitation = member.Status == WorkspaceMemberStatus.Invited;
        member.Status = WorkspaceMemberStatus.Disabled;
        member.InvitationRevokedDate = wasInvitation ? now : member.InvitationRevokedDate;
        member.InvitationTokenHash = null;
        member.ModifiedDate = now;
        member.ModifiedBy = session.UserAuthId!;
        Db.Update(member);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId=workspace.Id, Category="membership", Action=wasInvitation ? "invitation.revoked" : "member.removed", ActorId=session.UserAuthId!,
            SubjectId=member.Id, CreatedDate=now,
        });
        return new EmptyResponse();
    }

    public async Task<object> Any(CreateCheckoutSession request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        AssertCanManageBilling(context);
        var workspace = context.Workspace;
        var price = Db.SingleById<SaasPlanPrice>(request.PriceId)
            ?? throw new HttpError(404, "PriceNotFound", "The selected price was not found.");
        var version = Db.SingleById<SaasPlanVersion>(price.PlanVersionId)
            ?? throw new HttpError(404, "PlanVersionNotFound", "The selected plan version was not found.");
        var plan = Db.SingleById<SaasPlan>(version.PlanId)
            ?? throw new HttpError(404, "PlanNotFound", "The selected plan was not found.");
        if (!price.IsActive || version.Status != PlanVersionStatus.Published || plan.IsArchived || !plan.IsPublic || plan.IsContactSales)
            throw new HttpError(409, "PlanNotAvailable", "This plan is not available for self-serve checkout.");
        if (price.StripePriceId.IsNullOrEmpty())
            throw new HttpError(503, "StripePriceNotConfigured", "Add the Stripe Price ID in the SaaS administration area before starting checkout.");
        var url = await stripe.CreateCheckoutAsync(workspace, price,
            Request.GetBaseUrl().CombineWith("/billing?checkout=success&session_id={CHECKOUT_SESSION_ID}"),
            Request.GetBaseUrl().CombineWith("/pricing"), version.TrialDays);
        return new CreateBillingSessionResponse { Url = url };
    }

    public async Task<object> Any(ConfirmCheckoutSession request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        AssertCanManageBilling(context);
        var workspace = context.Workspace;
        var confirmed = await stripe.ConfirmCheckoutAsync(Db, workspace, request.SessionId);
        var subscription = Db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        if (confirmed)
            manager.EvaluateAccess(Db, workspace, subscription);
        return new ConfirmCheckoutSessionResponse {
            Confirmed = confirmed,
            SubscriptionStatus = subscription.Status.ToString(),
        };
    }

    public async Task<object> Any(CreateCustomerPortalSession request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        AssertCanManageBilling(context);
        var workspace = context.Workspace;
        var url = await stripe.CreatePortalAsync(workspace, Request.GetBaseUrl().CombineWith("/billing"));
        return new CreateBillingSessionResponse { Url = url };
    }

    public async Task<object> Any(StripeWebhook request)
    {
        var payload = await Request!.GetRawBodyAsync() ?? "";
        var envelope = stripe.ValidateWebhook(payload, Request.Headers["Stripe-Signature"] ?? "");
        if (Db.Exists<StripeEventInbox>(x => x.StripeEventId == envelope.EventId))
            return new EmptyResponse();
        var inbox = new StripeEventInbox {
            StripeEventId = envelope.EventId, EventType = envelope.EventType, PayloadJson = envelope.PayloadJson,
            Status = StripeInboxStatus.Pending, ReceivedDate = DateTime.UtcNow,
        };
        Db.Insert(inbox);
        jobs.EnqueueCommand<ProcessStripeEventCommand>(new ProcessStripeEvent { InboxId = inbox.Id });
        return new EmptyResponse();
    }

    public object Any(GetSaasAdmin request) => new GetSaasAdminResponse {
        TrialsEnabled = config.EnableTrials,
        TrialRequiresPaymentMethod = config.TrialRequiresPaymentMethod,
        DefaultTrialDays = config.DefaultTrialDays,
        StripeConfigured = stripe.IsConfigured,
        StripeCatalogProvisioningEnabled = stripe.IsCatalogProvisioningEnabled,
        StripeMode = !stripe.IsConfigured ? "Not configured" : stripe.IsLiveMode ? "Live" : "Sandbox",
        Plans = Db.Select<SaasPlan>().OrderBy(x => x.DisplayOrder).ToList(),
        Versions = Db.Select<SaasPlanVersion>().OrderByDescending(x => x.CreatedDate).ToList(),
        Prices = Db.Select<SaasPlanPrice>(), Features = Db.Select<SaasPlanFeature>(), Quotas = Db.Select<SaasPlanQuota>(),
        Workspaces = Db.Select<Workspace>().OrderByDescending(x => x.CreatedDate).Take(100).ToList(),
        RecentStripeEvents = Db.Select<StripeEventInbox>().OrderByDescending(x => x.ReceivedDate).Take(100).ToList(),
    };

    public object Any(GetSaasPlanDetails request) => manager.GetPlanDetails(Db, request.PlanId);

    public async Task<object> Any(GetSaasCoupons request) => new GetSaasCouponsResponse {
        StripeConfigured = stripe.IsConfigured,
        Results = await stripe.GetCouponsAsync(),
    };

    public async Task<object> Any(CreateSaasCoupon request)
    {
        ValidateCoupon(request);
        var session = await GetSessionAsync();
        var result = await stripe.CreateCouponAsync(request);
        Db.Insert(new SaasAuditEvent {
            Category = "coupon", Action = "coupon.created", ActorId = session.UserAuthId!,
            SubjectId = result.PromotionCodeId,
            DetailJson = new { result.Code, result.CouponId, result.PercentOff, result.AmountOff, result.Currency }.ToJson(),
            CreatedDate = DateTime.UtcNow,
        });
        return result;
    }

    public async Task<object> Any(DeactivateSaasCoupon request)
    {
        var session = await GetSessionAsync();
        var result = await stripe.DeactivateCouponAsync(request.PromotionCodeId);
        Db.Insert(new SaasAuditEvent {
            Category = "coupon", Action = "coupon.deactivated", ActorId = session.UserAuthId!,
            SubjectId = result.PromotionCodeId, DetailJson = new { result.Code, result.CouponId }.ToJson(),
            CreatedDate = DateTime.UtcNow,
        });
        return result;
    }

    public async Task<object> Any(ProvisionSaasPlanStripeCatalog request)
    {
        var session = await GetSessionAsync();
        request.Prices ??= [];
        var plan = Db.SingleById<SaasPlan>(request.PlanId)
            ?? throw new HttpError(404, "PlanNotFound", "The selected plan was not found.");
        var candidates = request.Prices.Where(x => x.IsActive && x.UnitAmount > 0 && x.StripePriceId.IsNullOrEmpty()).ToList();
        if (candidates.Count == 0)
            throw new HttpError(409, "StripePricesAlreadyMapped", "This plan has no unmapped positive prices to create in Stripe.");
        if (candidates.Any(x => x.Currency.IsNullOrEmpty() || x.Currency.Trim().Length != 3 || !x.Currency.Trim().All(char.IsLetter)))
            throw new HttpError(400, "InvalidStripePriceCurrency", "Stripe prices require a three-letter currency code.");
        if (candidates.GroupBy(x => $"{x.Currency.Trim().ToLowerInvariant()}:{x.Interval}").Any(x => x.Count() > 1))
            throw new HttpError(409, "DuplicateStripePrice", "Only one active price can be provisioned for each currency and billing interval.");

        var result = await stripe.ProvisionCatalogAsync(plan, request);
        Db.Insert(new SaasAuditEvent {
            Category = "stripe", Action = "catalog.provisioned", ActorId = session.UserAuthId!,
            SubjectId = plan.Id,
            DetailJson = new { result.StripeProductId, result.Livemode, result.ProductCreated,
                PricesCreated = result.Prices.Count(x => x.Created), PricesReused = result.Prices.Count(x => !x.Created) }.ToJson(),
            CreatedDate = DateTime.UtcNow,
        });
        return result;
    }

    public async Task<object> Any(SaveSaasPlanDraft request)
    {
        var session = await GetSessionAsync();
        return manager.SavePlanDraft(Db, session.UserAuthId!, request);
    }

    public async Task<object> Any(PublishSaasPlanDraft request)
    {
        var session = await GetSessionAsync();
        return manager.PublishPlanDraft(Db, session.UserAuthId!, request.PlanId);
    }

    public async Task<object> Any(SaveCustomerOverride request)
    {
        var session = await GetSessionAsync();
        request.Key = request.Key.Trim();
        if (!Db.Exists<Workspace>(x => x.Id == request.WorkspaceId))
            throw new HttpError(404, "WorkspaceNotFound", "The organization was not found.");
        if ((request.Enabled != null) == (request.QuotaUnits != null))
            throw new HttpError(400, "InvalidOverride", "Set either a feature value or a quota allowance, but not both.");
        if (request.Enabled != null && !config.Features.Any(x => x.Key.Equals(request.Key, StringComparison.OrdinalIgnoreCase)))
            throw new HttpError(400, "UnknownFeature", $"Feature '{request.Key}' is not registered in SaaS configuration.");
        if (request.QuotaUnits != null && request.Key != "workspace.seats" &&
            !config.Meters.Any(x => x.Key.Equals(request.Key, StringComparison.OrdinalIgnoreCase)))
            throw new HttpError(400, "UnknownMeter", $"Meter '{request.Key}' is not registered in SaaS configuration.");
        if (request.QuotaUnits < 0)
            throw new HttpError(400, "InvalidAllowance", "A quota allowance cannot be negative.");
        if (request.ValidUntil != null && request.ValidUntil <= DateTime.UtcNow)
            throw new HttpError(400, "InvalidOverrideExpiry", "Override expiry must be in the future.");
        var now = DateTime.UtcNow;
        var row = request.Id.IsNullOrEmpty()
            ? Db.Single<CustomerEntitlementOverride>(x => x.WorkspaceId == request.WorkspaceId && x.Key == request.Key)
                ?? new CustomerEntitlementOverride { CreatedDate = now, CreatedBy = session.UserAuthId! }
            : Db.SingleById<CustomerEntitlementOverride>(request.Id) ?? throw new HttpError(404, "OverrideNotFound", "Override not found.");
        row.PopulateWith(request);
        row.ModifiedDate = now;
        row.ModifiedBy = session.UserAuthId!;
        Db.Save(row);
        Db.Insert(new SaasAuditEvent { WorkspaceId = row.WorkspaceId, Category = "entitlement", Action = "override.saved", ActorId = session.UserAuthId!, SubjectId = row.Id, DetailJson = row.ToJson(), CreatedDate = now });
        return row;
    }

    public async Task<object> Any(DeleteCustomerOverride request)
    {
        var session = await GetSessionAsync();
        var row = Db.SingleById<CustomerEntitlementOverride>(request.Id)
            ?? throw new HttpError(404, "OverrideNotFound", "Override not found.");
        using var tx = Db.OpenTransaction();
        Db.DeleteById<CustomerEntitlementOverride>(row.Id);
        Db.Insert(new SaasAuditEvent {
            WorkspaceId = row.WorkspaceId, Category = "entitlement", Action = "override.deleted",
            ActorId = session.UserAuthId!, SubjectId = row.Id,
            DetailJson = new { row.Key, row.Enabled, row.QuotaUnits }.ToJson(), CreatedDate = DateTime.UtcNow,
        });
        tx.Commit();
        return new EmptyResponse();
    }

    private string BuildInvitationUrl(string token)
        => Request!.GetBaseUrl().CombineWith($"/team/invite?token={Uri.EscapeDataString(token)}");

    private void QueueInvitationEmail(Workspace workspace, WorkspaceMember member, string invitationUrl)
    {
        var safeWorkspace = WebUtility.HtmlEncode(workspace.Name);
        var safeProduct = WebUtility.HtmlEncode(product.ProductName);
        var safeUrl = WebUtility.HtmlEncode(invitationUrl);
        jobs.EnqueueCommand<SendEmailCommand>(new SendEmail {
            To=member.InvitedEmail!,
            Subject=$"Join {workspace.Name} on {product.ProductName}",
            BodyHtml=$"You have been invited to join <strong>{safeWorkspace}</strong> on {safeProduct}. " +
                $"<a href='{safeUrl}'>Review and accept the invitation</a>. This link expires in {Math.Max(1, config.InvitationExpiryDays)} days.",
        });
    }

    private static WorkspaceMemberInfo ToMemberInfo(WorkspaceMember member, string? invitationUrl = null)
        => new() {
            Id=member.Id, UserId=member.UserId, Email=member.InvitedEmail, Role=member.Role, Status=member.Status,
            InvitationEmailSent=member.InvitationSentDate != null, InvitationUrl=invitationUrl,
            InvitedDate=member.InvitedDate, InvitationExpiresAt=member.InvitationExpiresAt,
            InvitationExpired=member.Status == WorkspaceMemberStatus.Invited && member.InvitationExpiresAt <= DateTime.UtcNow,
            JoinedDate=member.JoinedDate,
        };

    private void SaveActiveWorkspacePreference(string userId, string workspaceId, DateTime now)
    {
        var preference = Db.SingleById<UserWorkspacePreference>(userId)
            ?? new UserWorkspacePreference { UserId=userId, CreatedDate=now, CreatedBy=userId };
        preference.ActiveWorkspaceId = workspaceId;
        preference.ModifiedDate = now;
        preference.ModifiedBy = userId;
        Db.Save(preference);
    }

    private static void AssertWorkspaceAdmin(WorkspaceContext context)
        => WorkspaceAuthorization.RequireAdmin(context);

    private static void AssertCanManageBilling(WorkspaceContext context)
        => WorkspaceAuthorization.RequireBilling(context);

    private void AssertInteractiveRequest()
    {
        if (WorkspaceContextResolver.IsApiKeyRequest(Request))
            throw new HttpError(403, "InteractiveSessionRequired", "API keys cannot list or change the signed-in user's active organization.");
    }

    private static void ValidateCoupon(CreateSaasCoupon request)
    {
        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
        if (request.Code.Length is < 3 or > 50 || !request.Code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            throw new HttpError(400, "InvalidCouponCode", "Coupon codes must be 3 to 50 letters, numbers, hyphens, or underscores.");
        if ((request.PercentOff != null) == (request.AmountOff != null))
            throw new HttpError(400, "InvalidCouponDiscount", "Choose either a percentage or a fixed-amount discount.");
        if (request.PercentOff is <= 0 or > 100)
            throw new HttpError(400, "InvalidCouponPercent", "Percentage discounts must be greater than 0 and no more than 100.");
        if (request.AmountOff is <= 0)
            throw new HttpError(400, "InvalidCouponAmount", "Fixed discounts must be greater than zero.");
        if (request.AmountOff != null && (request.Currency.IsNullOrEmpty() || request.Currency!.Trim().Length != 3 || !request.Currency.Trim().All(char.IsLetter)))
            throw new HttpError(400, "InvalidCouponCurrency", "Fixed discounts need a three-letter currency code.");
        if (request.Duration == CouponDuration.Repeating && request.DurationInMonths is < 1 or > 36)
            throw new HttpError(400, "InvalidCouponDuration", "Repeating discounts must run for 1 to 36 months.");
        if (request.Duration != CouponDuration.Repeating && request.DurationInMonths != null)
            throw new HttpError(400, "InvalidCouponDuration", "Only repeating discounts can specify a number of months.");
        if (request.MaxRedemptions is <= 0)
            throw new HttpError(400, "InvalidCouponRedemptions", "Maximum redemptions must be greater than zero.");
        if (request.ExpiresAt != null && request.ExpiresAt <= DateTime.UtcNow)
            throw new HttpError(400, "InvalidCouponExpiry", "Coupon expiry must be in the future.");
    }
}

public class ProcessStripeEvent { public string InboxId { get; set; } = ""; }

[Worker("stripe")]
public class ProcessStripeEventCommand(
    IDbConnectionFactory dbFactory,
    IStripeBillingGateway stripe,
    ISaasManager manager,
    INotificationManager notifications,
    ILogger<ProcessStripeEventCommand> logger)
    : AsyncCommand<ProcessStripeEvent>
{
    protected override async Task RunAsync(ProcessStripeEvent request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        var inbox = db.SingleById<StripeEventInbox>(request.InboxId);
        if (inbox == null || inbox.Status == StripeInboxStatus.Completed) return;
        try
        {
            inbox.Status = StripeInboxStatus.Processing;
            inbox.Attempts++;
            db.Update(inbox);
            await stripe.ApplyWebhookAsync(db, inbox, token);
            var audit = db.Single<SaasAuditEvent>(x => x.SubjectId == inbox.StripeEventId && x.Category == "billing");
            if (audit?.WorkspaceId != null)
            {
                var workspace = db.SingleById<Workspace>(audit.WorkspaceId);
                var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
                manager.EvaluateAccess(db, workspace, subscription);
                var owner = db.Single<WorkspaceMember>(x => x.WorkspaceId == workspace.Id && x.Role == WorkspaceMemberRole.Owner && x.Status == WorkspaceMemberStatus.Active);
                if (owner != null && inbox.EventType is "invoice.payment_failed" or "invoice.paid" or "customer.subscription.updated" or "customer.subscription.deleted")
                    notifications.Queue(db, workspace.Id, owner.UserId, workspace.BillingEmail ?? "", "billing.changed",
                        $"Billing status changed for {workspace.Name}",
                        $"The subscription is now {subscription.Status}. Open billing settings to review the current access policy.",
                        $"stripe:{inbox.StripeEventId}");
            }
            inbox.Status = StripeInboxStatus.Completed;
            inbox.ProcessedDate = DateTime.UtcNow;
            inbox.LastError = null;
            db.Update(inbox);
        }
        catch (Exception ex)
        {
            inbox.Status = StripeInboxStatus.Failed;
            inbox.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            db.Update(inbox);
            logger.LogError(ex, "Stripe event {EventId} failed", inbox.StripeEventId);
            throw;
        }
    }
}
