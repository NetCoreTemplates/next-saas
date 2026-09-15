# Observability and health

The template exposes stable correlation, health, metrics, audit, request-log, and operator surfaces without choosing a production telemetry vendor.

[Operations](README.md) · [Support operations](../features/support-operations.md)

## Health endpoints

- `GET /up` checks only that the process can answer. Use it for container liveness and basic rollout health.
- `GET /ready` executes a database query and a write/delete probe against the configured local file-store root. Use it for traffic admission.

Anonymous health responses are intentionally terse. Detailed dependency and queue state belongs in authenticated `/admin/operations`, `/admin/security`, `/admin-ui`, and structured logs.

When replacing `IFileStore`, replace the local-path readiness check with a bounded provider check that proves the permissions required by the application without creating excessive cost.

![Container Liveness and Readiness Probes](../assets/health-probes.png)

## Request correlation

Every response includes `X-Request-Id`. A valid incoming value of at most 128 characters is preserved; otherwise ASP.NET trace context supplies it. The identifier becomes `HttpContext.TraceIdentifier`, a logging scope field, and audit correlation data.

Return this identifier in customer-facing error support flows. Forward it through outbound provider calls and background-job metadata where practical.

![Sanitized Request ID Correlation linking Client to Structured Server Log](../assets/correlation.png)

## Request logs

ServiceStack `RequestLogsFeature` uses `SqliteRequestLogger`. Request-body tracking is enabled only in Development because bodies can contain credentials, billing metadata, and content. Stripe webhook DTOs are excluded.

Treat request logs as diagnostics, not metering, billing, or audit truth. Restrict access and define retention before production.

## Metrics and traces

`SaasTelemetry` exposes the `MyApp.Saas` `ActivitySource` and meter with counters for:

- usage accepted and quota/rate-limit rejection;
- notifications queued/failed;
- lifecycle completion/failure;
- audit writes/rejections;
- retention rows deleted/run failures.

Attach OpenTelemetry or a vendor listener in the host without changing product services. Add low-cardinality tags only; never use organization IDs, emails, API keys, filenames, or Stripe IDs as metric labels.

## Minimum alerts

- `/ready` failure or restart loop;
- elevated 5xx/latency by route family;
- failed Stripe inbox events or stale reconciliation;
- failed/exhausted notifications;
- failed/blocked lifecycle requests;
- retention run failures;
- persistent quota/rate-limit anomaly;
- PostgreSQL saturation, low disk, or old backups;
- webhook and SMTP provider failure rates.

Alert on sustained actionable conditions and link each alert to the relevant runbook.

## Related documentation

- [Background jobs and recovery](background-jobs-and-recovery.md)
- [Troubleshooting](troubleshooting.md)
- [Audit logs](../features/audit-logs.md)
