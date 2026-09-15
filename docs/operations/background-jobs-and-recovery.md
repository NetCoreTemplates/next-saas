# Background jobs and recovery

ServiceStack Background Jobs handles asynchronous provider work, cleanup, rollups, lifecycle transitions, and reconciliation.

[Operations](README.md) · [Background processing](../concepts/background-processing.md)

## Recurring commands

The application registers hourly commands to:

- expire abandoned quota reservations;
- build usage rollups and SaaS snapshots;
- queue quota-threshold notifications;
- process due organization lifecycle requests;
- expire export artifacts;
- reconcile Stripe subscriptions.

Data retention runs daily. Email, webhook, file deletion, notification, and lifecycle commands are queued as business events require them.

`JobsHostedService` starts the job system and ticks it within the ASP.NET process. If no healthy application instance is running, scheduled work does not progress.

## Inspection

- `/admin-ui` provides ServiceStack job administration and diagnostics.
- `/admin/operations` summarizes product-specific failed Stripe events, notifications, lifecycle work, pending reservations, and integration readiness.
- `/admin/security` shows retention runs and active support access.
- `/admin/customers` Customer 360 shows organization-scoped state needed to understand impact.

Use durable domain/inbox status as the business truth. A job record alone is not proof that a Stripe event, notification, or deletion completed.

![Operations Queue and Failed Work Inspection](../assets/operations-queue-failed-work.png)

## Retry procedure

1. identify the domain/inbox row and its organization;
2. correlate logs and audit with request ID, provider ID, and timestamp;
3. fix configuration/provider/data cause;
4. confirm the handler is replay-safe;
5. use the product-specific retry action when available;
6. verify the durable result, not only queue completion;
7. record any manual intervention.

Do not enqueue a brand-new business operation with a new idempotency key merely to clear an error. Preserve original event/reservation/provider identifiers.

![Idempotent Background Job Recovery](../assets/idempotent-job-recovery.png)

## Stalled work

- Pending reservation past expiry: confirm the expiry command is running; release only through quota policy.
- Failed Stripe inbox: verify signature was accepted, inspect provider object, then retry or reconcile.
- Failed notification: fix SMTP/configuration, respect maximum attempts, then retry.
- Blocked lifecycle request: inspect legal hold, subscription, file-store, and confirmation state before retry.
- Old export artifact: run/inspect expiry; confirm both object and metadata transition.

## Multi-instance considerations

Confirm the selected ServiceStack job store and worker model provides the desired single-consumer/lease behavior across instances. Keep its persistent state on durable storage and isolate worker pools for workloads with different latency or concurrency needs.

## Related documentation

- [External services](external-services.md)
- [Retention and lifecycle](retention-and-lifecycle.md)
- [Add a background job](../development/add-a-background-job.md)
