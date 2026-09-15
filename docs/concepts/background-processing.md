# Background processing

Next SaaS uses ServiceStack Background Jobs for work that must survive request completion, retry safely, run later, or be inspected by operators.

[Documentation home](../README.md) · [Architecture](architecture.md)

## When to use a job

Use a Background Job when work is dependent on an external provider, slow or variable in duration, scheduled for later, safely retryable, destructive with visible progress, or periodic maintenance.

Keep small transactional database changes inside the request when the caller needs an immediate result. Do not enqueue merely to avoid defining correct transactional behavior.

![Idempotent Background Job Recovery Flow](../assets/idempotent-job-recovery.png)

## Command model

A job is a registered command with a small request payload:

```csharp
public class DeleteStoredFileWork
{
    public string FileId { get; set; } = "";
}

[Worker("storage")]
public class DeleteStoredFileCommand(...) : AsyncCommand<DeleteStoredFileWork>
{
    protected override async Task RunAsync(DeleteStoredFileWork request, CancellationToken token)
    {
        // Reload the file and current organization state, then act idempotently.
    }
}
```

Register the command in `Configure.BackgroundJobs.cs`, then enqueue it through `IBackgroundJobs`:

```csharp
jobs.EnqueueCommand<DeleteStoredFileCommand>(new DeleteStoredFileWork {
    FileId = row.Id,
});
```

Pass stable identifiers, not a serialized authorization decision or mutable customer record. Workers must reload current state.

## Worker queues

`[Worker("name")]` assigns commands to a logical queue where serialized or specialized processing is useful. The template uses named workers for storage and notifications. Choose queue names by operational workload, not by customer; avoid creating an unbounded queue per organization.

## Recurring commands

The host registers recurring work after ServiceStack initialization:

| Command | Schedule | Purpose |
| --- | --- | --- |
| `ExpireUsageReservationsCommand` | Hourly | Release abandoned admission capacity |
| `BuildUsageRollupsCommand` | Hourly | Rebuild daily usage projections |
| `BuildSaasDailySnapshotCommand` | Hourly | Capture operator business metrics |
| `QueueQuotaNotificationsCommand` | Hourly | Turn quota thresholds into notification intents |
| `ProcessDueWorkspaceLifecyclesCommand` | Hourly | Enqueue delayed organization operations |
| `ExpireDataExportsCommand` | Hourly | Delete expired archive objects and retain expiry evidence |
| `ReconcileStripeSubscriptionsCommand` | Hourly | Repair local Stripe subscription projections |
| `ApplyDataRetentionCommand` | Daily | Prune eligible data in bounded batches |

Recurring commands must be safe when invocations overlap or a prior run partially completed.

## Persist intent before enqueueing

For important workflows, the database row is the source of truth and the queue is a delivery mechanism:

1. Authorize the request.
2. Persist operation, status, actor, and idempotency/deduplication key.
3. Commit the transaction.
4. Enqueue the row ID.
5. Have the worker reload and claim current state.

This pattern is used for Stripe inbox events, notifications, stored files, and workspace lifecycle requests. A periodic scanner can re-enqueue due persisted work if an enqueue was lost.

![Webhook Ingestion and Asynchronous Execution Pipeline](../assets/webhook-flow.png)

## Idempotency and replay

Assume a command may execute more than once. A safe worker:

- returns when the target is already complete;
- validates the current state transition;
- uses stable provider idempotency or deduplication keys;
- does not double-apply usage adjustments;
- makes object deletion idempotent;
- records completion only after required effects succeed;
- retains enough state and error detail for diagnosis.

Operator retry APIs enqueue the same persisted operation rather than inventing a new business action.

## Tenant and authorization safety

Authorization happens when the customer requests an operation, but the worker still reloads current tenant state. A queued identifier is not trusted input.

Recheck policy that may change during a delay. Organization deletion checks legal hold at request time and again in the worker. File jobs derive their object key from the organization-owned database row rather than accepting it from the browser.

## Quota reservations in jobs

When asynchronous work consumes limited capacity:

1. Reserve maximum bounded capacity before enqueueing.
2. Persist the reservation or product record ID.
3. Settle actual usage on success.
4. Release it on terminal failure or cancellation.
5. Use expiry cleanup as a final safety net.

A retry must not create a second reservation for the same logical operation.

## Errors and operations

Commands store status, attempts, and a bounded last error on the domain or inbox record. `/admin/operations` surfaces failed Stripe events, notifications, and lifecycle work to the appropriate platform role. ServiceStack Admin provides the underlying job view.

Telemetry counts business outcomes. Customer-facing errors and logs share `X-Request-Id` for correlation. If a failure is intentionally non-fatal, persist a diagnostic or audit event that makes the lost side effect visible.

## Cancellation and shutdown

Pass the supplied `CancellationToken` through file, network, and provider operations. Cancellation may interrupt any await, so persisted state and idempotency must permit a later retry.

Do not use in-memory timers or fire-and-forget tasks for customer workflows. They disappear when the process restarts.

## Adding a command

1. Define a minimal request containing stable IDs.
2. Implement `AsyncCommand<T>` and choose a worker queue if needed.
3. Register it with dependency injection.
4. Persist business intent before enqueueing.
5. Add a recurring scanner if future scheduling or recoverable delivery requires it.
6. Expose bounded status and operator retry behavior where useful.
7. Add audit and telemetry at business transitions.
8. Test replay, partial failure, cancellation, isolation, and terminal state.
9. Add the job to its module in `features.json`.

## Common mistakes

- Passing an entire customer object into a job and trusting its stale status.
- Treating enqueue success as business completion.
- Performing provider work without idempotency.
- Making retries duplicate usage or notifications.
- Holding an HTTP request open for a large export.
- Using an in-process timer for durable work.
- Omitting failed work from operator visibility.

## Related concepts

- [Architecture](architecture.md)
- [Organizations and tenancy](organizations-and-tenancy.md)
- [Usage and quotas](usage-and-quotas.md)
- [Verify and ship](../getting-started/07-verify-and-ship.md)
