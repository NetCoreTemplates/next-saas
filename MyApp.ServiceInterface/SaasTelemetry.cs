using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.ServiceInterface;

/// <summary>
/// Stable instrumentation surface for SaaS operations. OpenTelemetry and other
/// System.Diagnostics listeners can subscribe without product code taking a
/// dependency on a specific observability vendor.
/// </summary>
public static class SaasTelemetry
{
    public const string SourceName = "MyApp.Saas";
    public static readonly ActivitySource Activities = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> UsageRecorded = Meter.CreateCounter<long>("saas.usage.recorded", "units");
    public static readonly Counter<long> QuotaRejected = Meter.CreateCounter<long>("saas.quota.rejected", "operations");
    public static readonly Counter<long> RateLimitRejected = Meter.CreateCounter<long>("saas.rate_limit.rejected", "requests");
    public static readonly Counter<long> NotificationsQueued = Meter.CreateCounter<long>("saas.notifications.queued", "notifications");
    public static readonly Counter<long> NotificationsFailed = Meter.CreateCounter<long>("saas.notifications.failed", "notifications");
    public static readonly Counter<long> LifecycleCompleted = Meter.CreateCounter<long>("saas.lifecycle.completed", "operations");
    public static readonly Counter<long> LifecycleFailed = Meter.CreateCounter<long>("saas.lifecycle.failed", "operations");
    public static readonly Counter<long> AuditWritten = Meter.CreateCounter<long>("saas.audit.written", "events");
    public static readonly Counter<long> AuditRejected = Meter.CreateCounter<long>("saas.audit.rejected", "events");
    public static readonly Counter<long> RetentionRowsDeleted = Meter.CreateCounter<long>("saas.retention.rows_deleted", "rows");
    public static readonly Counter<long> RetentionRunsFailed = Meter.CreateCounter<long>("saas.retention.runs_failed", "runs");
}
