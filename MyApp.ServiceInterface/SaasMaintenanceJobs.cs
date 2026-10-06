using System.Linq.Expressions;
using System.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public interface INotificationManager
{
    void Queue(IDbConnection db, string workspaceId, string userId, string recipient,
        string templateKey, string subject, string body, string deduplicationKey);
}

public class NotificationManager(NotificationConfig config, IBackgroundJobs jobs) : INotificationManager
{
    // Notifications are queued on a connection confined to their organization, which adds its condition to every
    // query and its WorkspaceId to every row
    public void Queue(IDbConnection db, string workspaceId, string userId, string recipient,
        string templateKey, string subject, string body, string deduplicationKey)
    {
        db.AssertConfinedTo(workspaceId);
        var now = DateTime.UtcNow;
        var inAppKey = deduplicationKey + ":in-app";
        if (config.EnableInApp && !db.Exists<NotificationDelivery>(x => x.DeduplicationKey == inAppKey) &&
            IsEnabled(db, userId, templateKey, NotificationChannel.InApp))
        {
            db.Insert(new NotificationDelivery {
                UserId = userId, Recipient = userId, TemplateKey = templateKey,
                Channel = NotificationChannel.InApp, Subject = subject, Body = body,
                DeduplicationKey = inAppKey, Status = NotificationDeliveryStatus.Delivered,
                DeliveredDate = now,
            });
        }
        var emailKey = deduplicationKey + ":email";
        if (config.Provider != EmailProvider.Disabled && !recipient.IsNullOrEmpty() && !db.Exists<NotificationDelivery>(x => x.DeduplicationKey == emailKey) &&
            IsEnabled(db, userId, templateKey, NotificationChannel.Email))
        {
            var isDevelopment = config.Provider == EmailProvider.Development;
            var delivery = new NotificationDelivery {
                UserId = userId, Recipient = recipient, TemplateKey = templateKey,
                Channel = NotificationChannel.Email, Subject = subject, Body = body,
                DeduplicationKey = emailKey,
                Status = isDevelopment ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Pending,
                ProviderId = isDevelopment ? "development-inbox" : null,
                DeliveredDate = isDevelopment ? now : null,
            };
            db.Insert(delivery);
            SaasTelemetry.NotificationsQueued.Add(1,
                new KeyValuePair<string, object?>("channel", NotificationChannel.Email.ToString()));
            if (!isDevelopment)
                jobs.EnqueueForWorkspace<ProcessNotificationDeliveryCommand>(workspaceId,
                    new ProcessNotificationDelivery { WorkspaceId = workspaceId, DeliveryId = delivery.Id });
        }
    }

    private static bool IsEnabled(IDbConnection db, string userId, string templateKey, NotificationChannel channel)
    {
        var preferences = db.Select<NotificationPreference>(x => x.UserId == userId &&
                x.Channel == channel && (x.TemplateKey == "*" || x.TemplateKey == templateKey))
            .OrderByDescending(x => x.TemplateKey == templateKey).ToList();
        return preferences.FirstOrDefault()?.Enabled ?? true;
    }
}

public class ProcessNotificationDelivery
{
    public string WorkspaceId { get; set; } = default!;
    public string DeliveryId { get; set; } = default!;
}

[Worker("notifications")]
public class ProcessNotificationDeliveryCommand(
    IDbConnectionFactory dbFactory,
    IBackgroundJobs jobs,
    IServiceProvider services,
    NotificationConfig config) : AsyncCommand<ProcessNotificationDelivery>
{
    protected override Task RunAsync(ProcessNotificationDelivery request, CancellationToken token)
    {
        using var db = dbFactory.OpenForWorkspace(request.WorkspaceId, "notification-job");
        var delivery = db.SingleById<NotificationDelivery>(request.DeliveryId);
        if (delivery == null || delivery.Status == NotificationDeliveryStatus.Delivered) return Task.CompletedTask;
        if (delivery.Attempts >= config.MaxAttempts)
            throw new InvalidOperationException("The notification delivery has exhausted its retry allowance.");
        try
        {
            delivery.Status = NotificationDeliveryStatus.Sending;
            delivery.Attempts++;
            db.Update(delivery);
            if (delivery.Channel == NotificationChannel.Email)
            {
                if (services.GetService(typeof(SmtpConfig)) == null)
                    throw new InvalidOperationException("Email notifications are enabled but SMTP is not configured.");
                jobs.EnqueueCommand<SendEmailCommand>(new SendEmail {
                    To = delivery.Recipient, Subject = delivery.Subject, BodyHtml = delivery.Body,
                    WorkspaceId = delivery.WorkspaceId, DeliveryId = delivery.Id,
                });
            }
            delivery.Status = NotificationDeliveryStatus.Sending;
            delivery.ProviderId = "smtp-job";
            delivery.LastError = null;
            db.Update(delivery);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SaasTelemetry.NotificationsFailed.Add(1,
                new KeyValuePair<string, object?>("channel", delivery.Channel.ToString()));
            delivery.Status = NotificationDeliveryStatus.Failed;
            delivery.LastError = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
            db.Update(delivery);
            throw;
        }
    }
}

public class ExpireUsageReservationsCommand(IDbConnectionFactory dbFactory) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("reservation-expiry");
        if (!db.TableExists<UsageReservation>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        foreach (var reservation in db.Select<UsageReservation>(x => x.Status == UsageReservationStatus.Pending && x.ExpiresAt <= now))
        {
            db.RunInTransaction(() => {
                // Skipped when it was settled or released after it was selected
                if (SaasManager.ClaimReservation(db, reservation.Id, () => new UsageReservation { Status = UsageReservationStatus.Expired }))
                    SaasManager.CloseReservedUnits(db, reservation.WorkspaceId, reservation.UsagePeriodId,
                        reservation.ReservedUnits, 0, long.MaxValue, "reservation-expiry", now);
            });
        }
        return Task.CompletedTask;
    }
}

public class BuildUsageRollupsCommand(IDbConnectionFactory dbFactory, SaasConfig config) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        var days = Math.Clamp(config.AnalyticsRetentionDays, 1, 730);
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        List<string> workspaceIds;
        using (var db = dbFactory.OpenAcrossWorkspaces("usage-rollup"))
        {
            if (!db.TableExists<UsageEvent>()) return Task.CompletedTask;
            workspaceIds = db.ColumnDistinct<string>(db.From<UsageEvent>().Where(x => x.RecordedDate >= from).Select(x => x.WorkspaceId)).ToList();
        }

        // One organization at a time, on a connection confined to it. Memory is bounded by the largest organization
        // instead of all of them, and a rollup can't be read from or written to another organization.
        foreach (var workspaceId in workspaceIds)
        {
            token.ThrowIfCancellationRequested();
            using var db = dbFactory.OpenForWorkspace(workspaceId, "usage-rollup");
            BuildRollups(db, from);
        }
        return Task.CompletedTask;
    }

    private void BuildRollups(IDbConnection db, DateTime from)
    {
        var events = db.Select<UsageEvent>(x => x.RecordedDate >= from);
        var existing = db.Select<UsageDailyRollup>(x => x.Date >= from)
            .ToDictionary(x => (x.Date.Date, x.MeterKey, x.DimensionType, x.DimensionValue));
        var rollups = events
            .GroupBy(x => new { Date = x.RecordedDate.Date, x.MeterKey })
            .Select(x => new { x.Key.Date, x.Key.MeterKey, DimensionType = "workspace", DimensionValue = "all", Units = x.Sum(y => y.Units), Count = x.LongCount() })
            .Concat(events
                .Where(x => config.Meters.LastOrDefault(m => m.Key.Equals(x.MeterKey, StringComparison.OrdinalIgnoreCase))?.AllowUserBreakdown != false)
                .GroupBy(x => new { Date = x.RecordedDate.Date, x.MeterKey, x.RecordedBy })
                .Select(x => new { x.Key.Date, x.Key.MeterKey, DimensionType = "user", DimensionValue = x.Key.RecordedBy, Units = x.Sum(y => y.Units), Count = x.LongCount() }));

        using var tx = db.OpenTransaction();
        foreach (var group in rollups)
        {
            // New rollups get their organization from the connection
            if (!existing.TryGetValue((group.Date, group.MeterKey, group.DimensionType, group.DimensionValue), out var row))
                row = new UsageDailyRollup {
                    Date = group.Date, MeterKey = group.MeterKey,
                    DimensionType = group.DimensionType, DimensionValue = group.DimensionValue,
                };
            if (row.Units == group.Units && row.EventCount == group.Count && row.ModifiedDate != default) continue;
            row.Units = group.Units;
            row.EventCount = group.Count;
            db.Save(row);
        }
        tx.Commit();
    }
}

/// <summary>
/// Records the platform's metrics for today, which is the history its trends are charted from
/// </summary>
public class BuildSaasDailySnapshotCommand(IDbConnectionFactory dbFactory, SaasConfig config) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("saas-snapshot");
        if (!db.TableExists<SaasDailySnapshot>()) return Task.CompletedTask;
        var date = DateTime.UtcNow.Date;
        var metrics = new List<(string Key, string Dimension, decimal Value)> {
            ("workspaces.active", "all", db.Count<Workspace>(x => x.Status == WorkspaceStatus.Active)),
            ("subscriptions.active", "all", db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Active)),
            ("subscriptions.trialing", "all", db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Trialing)),
            ("subscriptions.past_due", "all", db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.PastDue)),
            ("subscriptions.canceled", "all", db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Canceled)),
            ("storage.files", "all", db.Count<StoredFile>(x => x.Status == StoredFileStatus.Available)),
            ("storage.bytes", "all", db.Select<StoredFile>(x => x.Status == StoredFileStatus.Available).Sum(x => x.ByteLength)),
            ("operations.stripe_failed", "all", db.Count<StripeEventInbox>(x => x.Status == StripeInboxStatus.Failed)),
        };
        metrics.AddRange(SaasRevenue.SnapshotMetrics(SaasRevenue.Calculate(db, config.DefaultCurrency)));
        foreach (var metric in metrics)
        {
            var row = db.Single<SaasDailySnapshot>(x => x.Date == date && x.MetricKey == metric.Key && x.Dimension == metric.Dimension)
                ?? new SaasDailySnapshot { Date = date, MetricKey = metric.Key, Dimension = metric.Dimension };
            row.Value = metric.Value;
            db.Save(row);
        }
        return Task.CompletedTask;
    }
}

public class QueueQuotaNotificationsCommand(IDbConnectionFactory dbFactory, INotificationManager notifications) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("quota-notification-job");
        if (!db.TableExists<SaasAuditEvent>()) return Task.CompletedTask;
        var warnings = db.Select<SaasAuditEvent>(x => x.Category == "usage" && x.Action == "quota.warning")
            .OrderByDescending(x => x.CreatedDate).Take(500);
        foreach (var warning in warnings.Where(x => x.WorkspaceId != null))
        {
            // Each organization's notifications are queued on a connection confined to it
            using var workspaceDb = dbFactory.OpenForWorkspace(warning.WorkspaceId!, "quota-notification-job");
            var workspace = workspaceDb.SingleById<Workspace>(warning.WorkspaceId);
            var owner = workspaceDb.Single<WorkspaceMember>(x => x.Role == WorkspaceMemberRole.Owner && x.Status == WorkspaceMemberStatus.Active);
            if (workspace == null || owner == null) continue;
            notifications.Queue(workspaceDb, workspace.Id, owner.UserId, workspace.BillingEmail ?? "", "quota.threshold",
                $"{workspace.Name} is approaching a usage limit",
                "An organization usage meter crossed a configured warning threshold. Review usage and plan capacity.",
                $"audit:{warning.Id}");
        }
        return Task.CompletedTask;
    }
}

public class ProcessDueWorkspaceLifecyclesCommand(IDbConnectionFactory dbFactory, IBackgroundJobs jobs) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("lifecycle-job");
        if (!db.TableExists<WorkspaceLifecycleRequest>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        var due = db.Select<WorkspaceLifecycleRequest>(x => x.Status == LifecycleRequestStatus.Scheduled && x.ScheduledAt <= now);
        foreach (var operation in due)
            jobs.EnqueueForWorkspace<ProcessWorkspaceLifecycleCommand>(operation.WorkspaceId, new ProcessWorkspaceLifecycle { WorkspaceId = operation.WorkspaceId, RequestId = operation.Id });
        return Task.CompletedTask;
    }
}

public class ExpireDataExportsCommand(IDbConnectionFactory dbFactory, IFileStore files) : AsyncCommand<NoArgs>
{
    protected override async Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("export-expiry-job");
        if (!db.TableExists<DataExportArtifact>()) return;

        var expired = db.Select<DataExportArtifact>(x => x.ExpiresAt <= DateTime.UtcNow && x.ExpiredAt == null);
        foreach (var artifact in expired)
        {
            try
            {
                await files.ForWorkspace(artifact.WorkspaceId).DeleteAsync(artifact.ObjectKey, token);
                artifact.ExpiredAt = DateTime.UtcNow;
                artifact.LastError = null;
                db.Update(artifact);
                db.Insert(new SaasAuditEvent {
                    WorkspaceId = artifact.WorkspaceId, Category = "lifecycle", Action = "export.expired",
                    UserId = "export-expiry-job", SubjectId = artifact.Id, CreatedDate = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                artifact.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
                db.Update(artifact);
                db.Insert(new SaasAuditEvent {
                    WorkspaceId = artifact.WorkspaceId,
                    Category = "lifecycle",
                    Action = "export.expiry-failed",
                    UserId = "export-expiry-job",
                    SubjectId = artifact.Id,
                    Outcome = "Failed",
                    Reason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000],
                    CreatedDate = DateTime.UtcNow,
                });
                throw;
            }
        }
    }
}

public static class DataRetentionPolicy
{
    public static int EffectiveDays(WorkspaceRetentionPolicy? policy, Func<WorkspaceRetentionPolicy, int?> value, int fallback) =>
        policy == null ? fallback : value(policy) ?? fallback;

    public static bool IsExpired(WorkspaceRetentionPolicy? policy, DateTime now, DateTime date,
        Func<WorkspaceRetentionPolicy, int?> value, int fallback) =>
        policy?.LegalHold != true && date <= now.AddDays(-EffectiveDays(policy, value, fallback));
}

/// <summary>
/// Deletes rows that are past their retention period, without loading tables to decide which.
/// Each organization with its own policy is purged with that policy's period, unless it's on a legal hold.
/// Every other organization, and the platform's own tables, are purged with the configured default period.
/// </summary>
public sealed class RetentionPurge(IDbConnection db, List<WorkspaceRetentionPolicy> policies, DateTime now, int batchSize)
{
    private readonly List<string> withPolicy = policies.Select(x => x.WorkspaceId).ToList();

    /// <summary>Purge a table owned by organizations</summary>
    public int Owned<T>(Func<WorkspaceRetentionPolicy, int?> days, int defaultDays,
        Func<DateTime, Expression<Func<T, bool>>> expired) where T : IHasWorkspaceId =>
        Purge(days, defaultDays, expired, workspaceId => x => x.WorkspaceId == workspaceId,
            x => !Sql.In(x.WorkspaceId, withPolicy));

    /// <summary>Purge a table of the platform, which isn't owned by organizations</summary>
    public int Platform<T>(int defaultDays, Func<DateTime, Expression<Func<T, bool>>> expired) =>
        DeleteBatch(db.From<T>().Where(expired(now.AddDays(-defaultDays))), batchSize);

    private int Purge<T>(Func<WorkspaceRetentionPolicy, int?> days, int defaultDays,
        Func<DateTime, Expression<Func<T, bool>>> expired,
        Func<string, Expression<Func<T, bool>>> ofOrganization, Expression<Func<T, bool>> ofEveryoneElse)
    {
        var deleted = 0;
        foreach (var policy in policies.Where(x => !x.LegalHold))
        {
            var cutoff = now.AddDays(-DataRetentionPolicy.EffectiveDays(policy, days, defaultDays));
            deleted += DeleteBatch(db.From<T>().Where(ofOrganization(policy.WorkspaceId)).And(expired(cutoff)), batchSize - deleted);
        }

        var q = db.From<T>().Where(expired(now.AddDays(-defaultDays)));
        if (withPolicy.Count > 0)
            q.And(ofEveryoneElse);
        return deleted + DeleteBatch(q, batchSize - deleted);
    }

    private int DeleteBatch<T>(SqlExpression<T> q, int limit)
    {
        if (limit <= 0) return 0;
        var primaryKey = db.GetDialectProvider().GetQuotedColumnName(typeof(T).GetModelMetadata().PrimaryKey.FieldName);
        var ids = db.Column<string>(q.Select(primaryKey).Limit(limit));
        // Deleted in chunks that fit within every provider's limit on parameters
        return ids.Chunk(500).Sum(chunk => db.DeleteByIds<T>(chunk));
    }
}

public class ApplyDataRetentionCommand(IDbConnectionFactory dbFactory, SaasConfig config) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.OpenAcrossWorkspaces("retention-job");
        if (!db.TableExists<DataRetentionRun>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        var run = new DataRetentionRun();
        run.Id = db.Insert(run, selectIdentity: true);
        try
        {
            // Rows are found and deleted by the database, in batches. Organizations with their own policy are
            // purged with it, and everyone else with the configured defaults, see RetentionPurge.
            var purge = new RetentionPurge(db, db.Select<WorkspaceRetentionPolicy>(), now, config.RetentionBatchSize);
            run.UsageEventsDeleted = purge.Owned<UsageEvent>(p => p.AnalyticsRetentionDays, config.AnalyticsRetentionDays,
                cutoff => x => x.RecordedDate <= cutoff);
            run.UsageRollupsDeleted = purge.Owned<UsageDailyRollup>(p => p.AnalyticsRetentionDays, config.AnalyticsRetentionDays,
                cutoff => x => x.Date <= cutoff);
            run.NotificationsDeleted = purge.Owned<NotificationDelivery>(p => p.NotificationRetentionDays, config.NotificationRetentionDays,
                cutoff => x => (x.Status == NotificationDeliveryStatus.Delivered || x.Status == NotificationDeliveryStatus.Suppressed) && x.ModifiedDate <= cutoff);
            run.StoredFileRowsDeleted = purge.Owned<StoredFile>(p => p.DeletedFileRetentionDays, config.DeletedFileRetentionDays,
                cutoff => x => x.Status == StoredFileStatus.Deleted && x.DeletedDate != null && x.DeletedDate <= cutoff);
            run.ExportRowsDeleted = purge.Owned<DataExportArtifact>(p => p.LifecycleHistoryRetentionDays, config.LifecycleHistoryRetentionDays,
                cutoff => x => x.ExpiredAt != null && x.ExpiredAt <= cutoff);
            // A lifecycle request is kept while one of its exports still exists
            var withExports = db.From<DataExportArtifact>().Select(x => x.LifecycleRequestId);
            run.LifecycleRowsDeleted = purge.Owned<WorkspaceLifecycleRequest>(p => p.LifecycleHistoryRetentionDays, config.LifecycleHistoryRetentionDays,
                cutoff => x => (x.Status == LifecycleRequestStatus.Completed || x.Status == LifecycleRequestStatus.Canceled) &&
                    x.CompletedAt != null && x.CompletedAt <= cutoff && !Sql.In(x.Id, withExports));
            run.AuditRowsDeleted = purge.Owned<SaasAuditEvent>(p => p.AuditRetentionDays, config.AuditRetentionDays,
                    cutoff => x => x.CreatedDate <= cutoff)
                + purge.Platform<PlatformAuditEvent>(config.AuditRetentionDays, cutoff => x => x.CreatedDate <= cutoff);

            run.Status = "Completed";
            run.CompletedAt = DateTime.UtcNow;
            db.Update(run);
            SaasTelemetry.RetentionRowsDeleted.Add(run.UsageEventsDeleted + run.UsageRollupsDeleted + run.NotificationsDeleted +
                run.StoredFileRowsDeleted + run.ExportRowsDeleted + run.LifecycleRowsDeleted + run.AuditRowsDeleted);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.LastError = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
            run.CompletedAt = DateTime.UtcNow;
            db.Update(run);
            SaasTelemetry.RetentionRunsFailed.Add(1);
            throw;
        }
    }
}

public class ReconcileStripeSubscriptionsCommand(
    IDbConnectionFactory dbFactory,
    IStripeBillingGateway stripe,
    ISaasManager manager) : AsyncCommand<NoArgs>
{
    protected override async Task RunAsync(NoArgs request, CancellationToken token)
    {
        if (!stripe.IsConfigured) return;
        using var db = dbFactory.OpenAcrossWorkspaces("stripe-reconcile-job");
        if (!db.TableExists<BillingSubscription>()) return;

        var subscriptions = db.Select<BillingSubscription>(x => x.StripeSubscriptionId != null);
        foreach (var subscription in subscriptions)
        {
            var workspace = db.SingleById<Workspace>(subscription.WorkspaceId);
            if (workspace == null || workspace.Status == WorkspaceStatus.Deleted) continue;
            // Each organization's subscription is reconciled on a connection confined to it
            using var workspaceDb = dbFactory.OpenForWorkspace(workspace.Id, "stripe-reconcile-job");
            try
            {
                if (await stripe.ReconcileSubscriptionAsync(workspaceDb, workspace, token))
                    manager.EvaluateAccess(workspaceDb, workspace, workspaceDb.GetSubscription());
            }
            catch (Exception ex)
            {
                workspaceDb.Insert(new SaasAuditEvent {
                    Category = "billing",
                    Action = "subscription.reconciliation-failed",
                    UserId = "stripe-reconciliation-job",
                    SubjectId = subscription.StripeSubscriptionId,
                    Outcome = "Failed",
                    Reason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000],
                    CreatedDate = DateTime.UtcNow,
                });
            }
        }
    }
}
