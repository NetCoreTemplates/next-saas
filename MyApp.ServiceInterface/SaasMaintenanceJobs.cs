using System.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public interface INotificationManager
{
    void Queue(IDbConnection db, string? workspaceId, string userId, string recipient,
        string templateKey, string subject, string body, string deduplicationKey);
}

public class NotificationManager(NotificationConfig config, IBackgroundJobs jobs) : INotificationManager
{
    public void Queue(IDbConnection db, string? workspaceId, string userId, string recipient,
        string templateKey, string subject, string body, string deduplicationKey)
    {
        var now = DateTime.UtcNow;
        var inAppKey = deduplicationKey + ":in-app";
        if (config.EnableInApp && !db.Exists<NotificationDelivery>(x => x.DeduplicationKey == inAppKey) &&
            IsEnabled(db, workspaceId, userId, templateKey, NotificationChannel.InApp))
        {
            db.Insert(new NotificationDelivery {
                WorkspaceId = workspaceId, UserId = userId, Recipient = userId, TemplateKey = templateKey,
                Channel = NotificationChannel.InApp, Subject = subject, Body = body,
                DeduplicationKey = inAppKey, Status = NotificationDeliveryStatus.Delivered,
                DeliveredDate = now, CreatedDate = now, ModifiedDate = now,
            });
        }
        var emailKey = deduplicationKey + ":email";
        if (config.Provider != EmailProvider.Disabled && !recipient.IsNullOrEmpty() && !db.Exists<NotificationDelivery>(x => x.DeduplicationKey == emailKey) &&
            IsEnabled(db, workspaceId, userId, templateKey, NotificationChannel.Email))
        {
            var isDevelopment = config.Provider == EmailProvider.Development;
            var delivery = new NotificationDelivery {
                WorkspaceId = workspaceId, UserId = userId, Recipient = recipient, TemplateKey = templateKey,
                Channel = NotificationChannel.Email, Subject = subject, Body = body,
                DeduplicationKey = emailKey,
                Status = isDevelopment ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Pending,
                ProviderId = isDevelopment ? "development-inbox" : null,
                DeliveredDate = isDevelopment ? now : null,
                CreatedDate = now, ModifiedDate = now,
            };
            db.Insert(delivery);
            SaasTelemetry.NotificationsQueued.Add(1,
                new KeyValuePair<string, object?>("channel", NotificationChannel.Email.ToString()));
            if (!isDevelopment)
                jobs.EnqueueCommand<ProcessNotificationDeliveryCommand>(new ProcessNotificationDelivery { DeliveryId = delivery.Id });
        }
    }

    private static bool IsEnabled(IDbConnection db, string? workspaceId, string userId, string templateKey, NotificationChannel channel)
    {
        var preferences = db.Select<NotificationPreference>(x => x.UserId == userId && x.Channel == channel)
            .Where(x => x.WorkspaceId == workspaceId && (x.TemplateKey == "*" || x.TemplateKey == templateKey))
            .OrderByDescending(x => x.TemplateKey == templateKey).ToList();
        return preferences.FirstOrDefault()?.Enabled ?? true;
    }
}

public class ProcessNotificationDelivery { public string DeliveryId { get; set; } = ""; }

[Worker("notifications")]
public class ProcessNotificationDeliveryCommand(
    IDbConnectionFactory dbFactory,
    IBackgroundJobs jobs,
    IServiceProvider services,
    NotificationConfig config) : AsyncCommand<ProcessNotificationDelivery>
{
    protected override Task RunAsync(ProcessNotificationDelivery request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        var delivery = db.SingleById<NotificationDelivery>(request.DeliveryId);
        if (delivery == null || delivery.Status == NotificationDeliveryStatus.Delivered) return Task.CompletedTask;
        if (delivery.Attempts >= config.MaxAttempts)
            throw new InvalidOperationException("The notification delivery has exhausted its retry allowance.");
        try
        {
            delivery.Status = NotificationDeliveryStatus.Sending;
            delivery.Attempts++;
            delivery.ModifiedDate = DateTime.UtcNow;
            db.Update(delivery);
            if (delivery.Channel == NotificationChannel.Email)
            {
                if (services.GetService(typeof(SmtpConfig)) == null)
                    throw new InvalidOperationException("Email notifications are enabled but SMTP is not configured.");
                jobs.EnqueueCommand<SendEmailCommand>(new SendEmail {
                    To = delivery.Recipient, Subject = delivery.Subject, BodyHtml = delivery.Body,
                    DeliveryId = delivery.Id,
                });
            }
            delivery.Status = NotificationDeliveryStatus.Sending;
            delivery.ProviderId = "smtp-job";
            delivery.LastError = null;
            delivery.ModifiedDate = DateTime.UtcNow;
            db.Update(delivery);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SaasTelemetry.NotificationsFailed.Add(1,
                new KeyValuePair<string, object?>("channel", delivery.Channel.ToString()));
            delivery.Status = NotificationDeliveryStatus.Failed;
            delivery.LastError = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
            delivery.ModifiedDate = DateTime.UtcNow;
            db.Update(delivery);
            throw;
        }
    }
}

public class ExpireUsageReservationsCommand(IDbConnectionFactory dbFactory) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<UsageReservation>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        foreach (var reservation in db.Select<UsageReservation>(x => x.Status == UsageReservationStatus.Pending && x.ExpiresAt <= now))
        {
            using var tx = db.OpenTransaction();
            var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == reservation.UsagePeriodId);
            if (aggregate != null)
            {
                aggregate.ReservedUnits = Math.Max(0, aggregate.ReservedUnits - reservation.ReservedUnits);
                aggregate.ModifiedDate = now;
                aggregate.ModifiedBy = "reservation-expiry";
                db.Update(aggregate);
            }
            reservation.Status = UsageReservationStatus.Expired;
            reservation.ModifiedDate = now;
            reservation.ModifiedBy = "reservation-expiry";
            db.Update(reservation);
            tx.Commit();
        }
        return Task.CompletedTask;
    }
}

public class BuildUsageRollupsCommand(IDbConnectionFactory dbFactory, SaasConfig config) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<UsageEvent>()) return Task.CompletedTask;
        var days = Math.Clamp(config.AnalyticsRetentionDays, 1, 730);
        var from = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var events = db.Select<UsageEvent>(x => x.RecordedDate >= from);
        var now = DateTime.UtcNow;
        using var tx = db.OpenTransaction();
        var rollups = events
            .GroupBy(x => new { x.WorkspaceId, Date = x.RecordedDate.Date, x.MeterKey })
            .Select(x => new { x.Key.WorkspaceId, x.Key.Date, x.Key.MeterKey, DimensionType = "workspace", DimensionValue = "all", Units = x.Sum(y => y.Units), Count = x.LongCount() })
            .Concat(events
                .Where(x => config.Meters.LastOrDefault(m => m.Key.Equals(x.MeterKey, StringComparison.OrdinalIgnoreCase))?.AllowUserBreakdown != false)
                .GroupBy(x => new { x.WorkspaceId, Date = x.RecordedDate.Date, x.MeterKey, x.RecordedBy })
                .Select(x => new { x.Key.WorkspaceId, x.Key.Date, x.Key.MeterKey, DimensionType = "user", DimensionValue = x.Key.RecordedBy, Units = x.Sum(y => y.Units), Count = x.LongCount() }));
        foreach (var group in rollups)
        {
            var row = db.Single<UsageDailyRollup>(x => x.WorkspaceId == group.WorkspaceId && x.Date == group.Date &&
                x.MeterKey == group.MeterKey && x.DimensionType == group.DimensionType && x.DimensionValue == group.DimensionValue);
            row ??= new UsageDailyRollup {
                WorkspaceId = group.WorkspaceId, Date = group.Date, MeterKey = group.MeterKey,
                DimensionType = group.DimensionType, DimensionValue = group.DimensionValue, CreatedDate = now,
            };
            row.Units = group.Units;
            row.EventCount = group.Count;
            row.ModifiedDate = now;
            row.ModifiedBy = "usage-rollup";
            db.Save(row);
        }
        tx.Commit();
        return Task.CompletedTask;
    }
}

public class BuildSaasDailySnapshotCommand(IDbConnectionFactory dbFactory) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<SaasDailySnapshot>()) return Task.CompletedTask;
        var date = DateTime.UtcNow.Date;
        var metrics = new Dictionary<string, decimal> {
            ["workspaces.active"] = db.Count<Workspace>(x => x.Status == WorkspaceStatus.Active),
            ["subscriptions.active"] = db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Active),
            ["subscriptions.trialing"] = db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.Trialing),
            ["subscriptions.past_due"] = db.Count<BillingSubscription>(x => x.Status == SubscriptionStatus.PastDue),
            ["storage.files"] = db.Count<StoredFile>(x => x.Status == StoredFileStatus.Available),
            ["storage.bytes"] = db.Select<StoredFile>(x => x.Status == StoredFileStatus.Available).Sum(x => x.ByteLength),
            ["operations.stripe_failed"] = db.Count<StripeEventInbox>(x => x.Status == StripeInboxStatus.Failed),
        };
        var now = DateTime.UtcNow;
        foreach (var metric in metrics)
        {
            var row = db.Single<SaasDailySnapshot>(x => x.Date == date && x.MetricKey == metric.Key && x.Dimension == "all")
                ?? new SaasDailySnapshot { Date = date, MetricKey = metric.Key, Dimension = "all", CreatedDate = now };
            row.Value = metric.Value;
            row.ModifiedDate = now;
            row.ModifiedBy = "saas-snapshot";
            db.Save(row);
        }
        return Task.CompletedTask;
    }
}

public class QueueQuotaNotificationsCommand(IDbConnectionFactory dbFactory, INotificationManager notifications) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<SaasAuditEvent>()) return Task.CompletedTask;
        var warnings = db.Select<SaasAuditEvent>(x => x.Category == "usage" && x.Action == "quota.warning")
            .OrderByDescending(x => x.CreatedDate).Take(500);
        foreach (var warning in warnings.Where(x => x.WorkspaceId != null))
        {
            var workspace = db.SingleById<Workspace>(warning.WorkspaceId);
            var owner = db.Single<WorkspaceMember>(x => x.WorkspaceId == warning.WorkspaceId && x.Role == WorkspaceMemberRole.Owner && x.Status == WorkspaceMemberStatus.Active);
            if (workspace == null || owner == null) continue;
            notifications.Queue(db, workspace.Id, owner.UserId, workspace.BillingEmail ?? "", "quota.threshold",
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
        using var db = dbFactory.Open();
        if (!db.TableExists<WorkspaceLifecycleRequest>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        var due = db.Select<WorkspaceLifecycleRequest>(x => x.Status == LifecycleRequestStatus.Scheduled && x.ScheduledAt <= now);
        foreach (var operation in due)
            jobs.EnqueueCommand<ProcessWorkspaceLifecycleCommand>(new ProcessWorkspaceLifecycle { RequestId = operation.Id });
        return Task.CompletedTask;
    }
}

public class ExpireDataExportsCommand(IDbConnectionFactory dbFactory, IFileStore files) : AsyncCommand<NoArgs>
{
    protected override async Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<DataExportArtifact>()) return;

        var expired = db.Select<DataExportArtifact>(x => x.ExpiresAt <= DateTime.UtcNow && x.ExpiredAt == null);
        foreach (var artifact in expired)
        {
            try
            {
                await files.DeleteAsync(artifact.ObjectKey, token);
                artifact.ExpiredAt = DateTime.UtcNow;
                artifact.LastError = null;
                artifact.ModifiedDate = DateTime.UtcNow;
                artifact.ModifiedBy = "export-expiry-job";
                db.Update(artifact);
                db.Insert(new SaasAuditEvent {
                    WorkspaceId = artifact.WorkspaceId, Category = "lifecycle", Action = "export.expired",
                    ActorId = "export-expiry-job", SubjectId = artifact.Id, CreatedDate = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                artifact.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
                artifact.ModifiedDate = DateTime.UtcNow;
                artifact.ModifiedBy = "export-expiry-job";
                db.Update(artifact);
                db.Insert(new SaasAuditEvent {
                    WorkspaceId = artifact.WorkspaceId,
                    Category = "lifecycle",
                    Action = "export.expiry-failed",
                    ActorId = "export-expiry-job",
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

public class ApplyDataRetentionCommand(IDbConnectionFactory dbFactory, SaasConfig config) : AsyncCommand<NoArgs>
{
    protected override Task RunAsync(NoArgs request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<DataRetentionRun>()) return Task.CompletedTask;
        var now = DateTime.UtcNow;
        var run = new DataRetentionRun { CreatedDate = now, ModifiedDate = now, CreatedBy = "retention-job", ModifiedBy = "retention-job" };
        run.Id = db.Insert(run, selectIdentity: true);
        try
        {
            var policies = db.Select<WorkspaceRetentionPolicy>().ToDictionary(x => x.WorkspaceId);
            WorkspaceRetentionPolicy? Policy(string? workspaceId) =>
                workspaceId != null && policies.TryGetValue(workspaceId, out var policy) ? policy : null;
            bool Expired(string? workspaceId, DateTime date, Func<WorkspaceRetentionPolicy, int?> value, int fallback) =>
                DataRetentionPolicy.IsExpired(Policy(workspaceId), now, date, value, fallback);

            foreach (var row in db.Select<UsageEvent>().Where(x => Expired(x.WorkspaceId, x.RecordedDate, p => p.AnalyticsRetentionDays, config.AnalyticsRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<UsageEvent>(row.Id); run.UsageEventsDeleted++; }
            foreach (var row in db.Select<UsageDailyRollup>().Where(x => Expired(x.WorkspaceId, x.Date, p => p.AnalyticsRetentionDays, config.AnalyticsRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<UsageDailyRollup>(row.Id); run.UsageRollupsDeleted++; }
            foreach (var row in db.Select<NotificationDelivery>().Where(x => x.Status is NotificationDeliveryStatus.Delivered or NotificationDeliveryStatus.Suppressed &&
                         Expired(x.WorkspaceId, x.ModifiedDate, p => p.NotificationRetentionDays, config.NotificationRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<NotificationDelivery>(row.Id); run.NotificationsDeleted++; }
            foreach (var row in db.Select<StoredFile>().Where(x => x.Status == StoredFileStatus.Deleted && x.DeletedDate != null &&
                         Expired(x.WorkspaceId, x.DeletedDate.Value, p => p.DeletedFileRetentionDays, config.DeletedFileRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<StoredFile>(row.Id); run.StoredFileRowsDeleted++; }
            foreach (var row in db.Select<DataExportArtifact>().Where(x => x.ExpiredAt != null &&
                         Expired(x.WorkspaceId, x.ExpiredAt.Value, p => p.LifecycleHistoryRetentionDays, config.LifecycleHistoryRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<DataExportArtifact>(row.Id); run.ExportRowsDeleted++; }
            foreach (var row in db.Select<WorkspaceLifecycleRequest>().Where(x => x.CompletedAt != null &&
                         x.Status is LifecycleRequestStatus.Completed or LifecycleRequestStatus.Canceled &&
                         !db.Exists<DataExportArtifact>(a => a.LifecycleRequestId == x.Id) &&
                         Expired(x.WorkspaceId, x.CompletedAt.Value, p => p.LifecycleHistoryRetentionDays, config.LifecycleHistoryRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<WorkspaceLifecycleRequest>(row.Id); run.LifecycleRowsDeleted++; }
            foreach (var row in db.Select<SaasAuditEvent>().Where(x => Expired(x.WorkspaceId, x.CreatedDate, p => p.AuditRetentionDays, config.AuditRetentionDays)).Take(config.RetentionBatchSize).ToList())
            { db.DeleteById<SaasAuditEvent>(row.Id); run.AuditRowsDeleted++; }

            run.Status = "Completed";
            run.CompletedAt = DateTime.UtcNow;
            run.ModifiedDate = run.CompletedAt.Value;
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
            run.ModifiedDate = run.CompletedAt.Value;
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
        using var db = dbFactory.Open();
        if (!db.TableExists<BillingSubscription>()) return;

        var subscriptions = db.Select<BillingSubscription>(x => x.StripeSubscriptionId != null);
        foreach (var subscription in subscriptions)
        {
            var workspace = db.SingleById<Workspace>(subscription.WorkspaceId);
            if (workspace == null || workspace.Status == WorkspaceStatus.Deleted) continue;
            try
            {
                if (await stripe.ReconcileSubscriptionAsync(db, workspace, token))
                {
                    var current = db.SingleById<BillingSubscription>(subscription.Id);
                    manager.EvaluateAccess(db, workspace, current);
                }
            }
            catch (Exception ex)
            {
                db.Insert(new SaasAuditEvent {
                    WorkspaceId = workspace.Id,
                    Category = "billing",
                    Action = "subscription.reconciliation-failed",
                    ActorId = "stripe-reconciliation-job",
                    SubjectId = subscription.StripeSubscriptionId,
                    Outcome = "Failed",
                    Reason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000],
                    CreatedDate = DateTime.UtcNow,
                });
            }
        }
    }
}
