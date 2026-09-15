# Add a meter and quota

A meter is the single measurement shared by quota enforcement, customer analytics, and operator diagnostics.

[Development recipes](README.md) · [Usage and quotas](../concepts/usage-and-quotas.md)

## 1. Choose the semantics

Select the behavior before writing code:

| Choice | Use when |
| --- | --- |
| `Counter` | accepted units accumulate, such as API requests |
| `Gauge` | current quantity can rise and fall, such as bytes stored |
| `ReservableCounter` | admission happens before fallible work, such as an upload |
| `BillingPeriod` | paid usage resets with projected Stripe boundaries |
| `CalendarMonth` | usage resets monthly in UTC |
| `Never` | lifetime/current state does not periodically reset |

Choose `Sum`, `Current`, or `Maximum` aggregation to match the commercial promise.

## 2. Register the meter

Add it to `Saas.Meters` in `MyApp/appsettings.json`:

```json
{
  "Key": "widgets.created",
  "DisplayName": "Widgets created",
  "UnitName": "widget",
  "DefaultEnforcement": "HardLimit",
  "Kind": "Counter",
  "Reset": "BillingPeriod",
  "Aggregation": "Sum",
  "AllowCustomerBreakdown": true,
  "AllowUserBreakdown": true
}
```

Keys and semantics are durable. Changing what an existing meter means corrupts historical interpretation; add a new key instead.

![Meter Definition and Registration](../assets/meter-definition.png)

## 3. Add plan allowances

Add the meter and included units to empty-state `plans.json`, or edit and publish plan drafts from the **Plans** tab at `/admin/plans`. `null` allowance means unlimited. Choose `HardLimit`, `SoftLimit`, or `TrackOnly` deliberately.

![Plan Allowance in Plan Editor](../assets/plan-allowance.png)

## 4. Record simple accepted work

For a single transactional operation, call `ISaasManager.RecordUsage` with organization, subscription, actor, positive units, and a stable idempotency key. Record only after the product mutation can commit consistently, and use the same database transaction when possible.

The public example endpoint is `POST /saas/usage`, protected by `api.access`.

![Record Usage and Quota Enforcement](../assets/record-usage.png)

## 5. Reserve fallible work

For uploads or provider work that may fail after admission:

```csharp
var reservation = manager.ReserveUsage(
    Db, workspace, subscription, userId,
    "widgets.created", units, operationId);

try
{
    var actualUnits = await PerformWorkAsync();
    manager.SettleUsage(Db, workspace, subscription, userId,
        reservation.Id, actualUnits);
}
catch
{
    manager.ReleaseUsage(Db, workspace, subscription, userId,
        reservation.Id);
    throw;
}
```

Expired pending reservations are released by a recurring job. Reuse the original business-operation key on retries.

## 6. Verify policy edges

Always test first acceptance, idempotent replay, exact limit, over-limit rejection, unlimited allowance, soft/track-only behavior, override precedence, period rollover, reservation settle/release/expiry, and concurrent admission. Add PostgreSQL concurrency coverage when changing the aggregate algorithm.

## Related documentation

- [Usage analytics](../features/usage-analytics.md)
- [Add a background job](add-a-background-job.md)
- [Testing](testing.md)
