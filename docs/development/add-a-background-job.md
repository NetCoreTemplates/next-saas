# Add a background job

Use ServiceStack Background Jobs for work that is slow, retryable, scheduled, or should survive the initiating request.

[Development recipes](README.md) · [Background processing](../concepts/background-processing.md)

## 1. Define a small command payload

Persist business state first, then enqueue only stable identifiers:

```csharp
public class ProcessWidget
{
    public string WidgetId { get; set; } = "";
}

[Worker("widgets")]
public class ProcessWidgetCommand(IDbConnectionFactory dbFactory)
    : AsyncCommand<ProcessWidget>
{
    protected override async Task RunAsync(
        ProcessWidget request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        var widget = db.SingleById<Widget>(request.WidgetId)
            ?? throw new InvalidOperationException("Widget was not found.");

        if (widget.Status == WidgetStatus.Completed)
            return;

        await ProcessAsync(widget, token);
        widget.Status = WidgetStatus.Completed;
        widget.ModifiedDate = DateTime.UtcNow;
        db.Update(widget);
    }
}
```

Do not serialize uploads, secrets, large documents, sessions, or an open database connection into a job request.

## 2. Register and enqueue

Register the command in `MyApp/Configure.BackgroundJobs.cs`:

```csharp
services.AddTransient<ProcessWidgetCommand>();
```

Enqueue after the referenced row exists:

```csharp
jobs.EnqueueCommand<ProcessWidgetCommand>(
    new ProcessWidget { WidgetId = widget.Id });
```

Use a named `[Worker]` when the workload should have an independent queue/concurrency boundary.

## 3. Add recurring work only when needed

Register recurring commands after the application host initializes:

```csharp
jobs.RecurringCommand<RepairWidgetsCommand>(Schedule.Hourly);
```

Recurring jobs must be safe when two ticks overlap or a prior run partially completed. Query bounded batches and checkpoint durable progress.

## Reliability rules

- Make the handler idempotent and return successfully when work is already complete.
- Re-read authoritative state inside the job; do not trust stale enqueue-time decisions.
- Preserve the tenant ID on every loaded/mutated resource.
- Use provider idempotency keys for external side effects.
- Store attempts/status/error on domain or inbox records when operators need recovery visibility.
- Compensate quota reservations when terminal work fails.
- Pass and honor cancellation tokens.

Use `/admin-ui` for general job inspection and `/admin/operations` for product-specific failed queues and safe retries.

## Verify

Test successful execution, duplicate delivery, missing/already-complete records, transient failure and retry, terminal failure, cancellation, tenant isolation, quota compensation, and recurring overlap. Run the application long enough to exercise the real hosted worker.

## Related documentation

- [Notifications](../features/notifications.md)
- [Billing and subscriptions](../features/billing-and-subscriptions.md)
- [Data lifecycle](../features/data-lifecycle.md)
