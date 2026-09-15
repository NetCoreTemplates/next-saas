# Usage and quotas

The quota system separates immutable evidence of consumption from fast, transactionally updated admission state. It enforces limits locally without relying on Stripe aggregation.

[Documentation home](../README.md) · [Plans and entitlements](plans-and-entitlements.md)

## Meter definitions and allowances

`Saas.Meters` defines stable meter behavior. `SaasPlanQuota` assigns an allowance and enforcement policy to a plan version. An active customer override may replace that allowance.

Canonical values are integers. Storage is measured in bytes; currency is not usage.

The included meters are:

| Key | Kind | Meaning |
| --- | --- | --- |
| `documents.stored` | Gauge | Current retained document count |
| `storage.bytes` | Gauge | Current stored byte total |
| `documents.uploaded` | Counter | Successful uploads in the billing period |
| `api.requests` | Counter | Accepted API request units in the billing period |
| `workspace.seats` | Gauge | Current non-disabled memberships |

## Meter kinds

### Counter

A counter records accepted activity and normally resets with a billing period or calendar month. It increases through immutable `UsageEvent` records.

Examples: API requests, reports generated, messages sent.

### Gauge

A gauge represents a current retained value and can increase or decrease. It normally never resets.

Examples: stored objects, storage bytes, seats, active projects.

Public `RecordUsage` rejects gauge changes. Product operations and privileged corrections use explicit adjustments with idempotency, actor, and reason.

### Reservation

A reservation admits work before its final cost is known. `ReservedUnits` counts against available capacity until the operation settles, releases, or expires.

Examples: uploads, batch jobs, generation work with a bounded maximum.

## Persistence model

- `UsagePeriod` identifies the organization, meter, time window, allowance, enforcement, and source.
- `UsageAggregate` provides fast used, reserved, peak, and last-event state.
- `UsageEvent` is immutable accepted consumption evidence.
- `UsageReservation` records pending and final admission state.
- `UsageDailyRollup` supports bounded analytics queries.

The event/aggregate write is performed transactionally. A conditional aggregate update prevents two concurrent requests from both consuming the final unit.

![Quota Enforcement Boundary](../assets/quota-boundary.png)

## Effective allowance

For each meter, `ISaasManager` resolves:

1. an active customer `QuotaUnits` override;
2. the effective plan version’s `IncludedUnits`;
3. no allowance when the plan intentionally represents unlimited capacity.

The resulting `UsagePeriod` records whether the source is the plan or an override. Responses expose the source for customer and operator diagnostics.

## Enforcement

| Policy | Behavior |
| --- | --- |
| HardLimit | Reject an operation that would exceed allowance |
| SoftLimit | Record usage while surfacing capacity pressure |
| MeteredOverage | Record usage for downstream billing/export policy without blocking at the included amount |

Stripe meter events, if added, are an export of local usage—not the enforcement ledger. Remote aggregation is asynchronous and unsuitable for admission decisions.

## Idempotency

Every logical consumption needs a stable idempotency key unique within its organization. A retry with the same key returns the existing accepted result without consuming again.

Generate the key at the edge of the logical operation and propagate it through retries and jobs. Do not generate a new key inside each retry attempt.

## Reserve, settle, release

The file upload example demonstrates fallible work:

1. Reserve one `documents.stored` unit.
2. Reserve the declared or maximum `storage.bytes` amount.
3. Insert pending metadata with an opaque object key.
4. Stream the file.
5. Settle byte capacity to the actual stored length.
6. Settle the document reservation.
7. Mark the file available and record analytical upload activity.

If work fails, pending reservations are released and partial content is removed. If settlement succeeded before a later failure, compensating gauge adjustments restore capacity. Hourly cleanup expires abandoned pending reservations.

Deletion runs asynchronously, removes the object idempotently, writes linked negative gauge adjustments, and marks metadata deleted.

![Capacity Reservation and Settlement Flow](../assets/reservation-flow.png)

## Periods and reset behavior

`MeterReset` may be:

- `BillingPeriod` — aligned to the local subscription period;
- `CalendarMonth` — aligned to calendar boundaries;
- `Never` — retained gauge state.

When a counter period elapses, a new period and aggregate are created. Old immutable events remain available according to retention policy. Rollover, when enabled for a plan quota, contributes eligible unused capacity to the next allowance.

## Warnings and rejection

Crossing configured `Saas.QuotaWarningPercentages` writes deduplicatable audit/notification intent. A rejected hard-limit operation writes diagnostic audit state and returns a stable quota error without an accepted usage event.

Quota warnings do not raise the limit. Customer upgrades and audited overrides change effective allowance.

![Customer Quota and Usage Telemetry](../assets/customer-usage.png)

## Analytics

Usage responses expose:

- used, reserved, peak, allowance, and remaining units;
- percentage used and enforcement;
- period boundaries and reset policy;
- meter kind and allowance source.

Analytics add daily series, top users, rejected-operation count, and a labeled projection. CSV export reconciles to the immutable event ledger for the requested window. A gauge can have current state without counter-like activity in that window.

## Adding quota-controlled work

Choose the pattern before writing product code:

- known, immediate activity → idempotent counter event;
- retained resource → gauge adjustment tied to create/delete lifecycle;
- uncertain or asynchronous cost → reserve, settle, release;
- informational threshold → soft limit;
- billable excess → local event plus a durable provider export.

Then add exact-limit, over-limit, replay, concurrency, compensation, and cross-organization tests.

## Common mistakes

- Counting database rows on every request instead of maintaining an aggregate.
- Calling Stripe to decide whether a request is allowed.
- Using floating-point canonical units.
- Incrementing a gauge through the public counter API.
- Creating a new idempotency key for each retry.
- Performing expensive work before reserving hard-limited capacity.
- Deleting immutable evidence merely because a period reset.

## Related concepts

- [Plans and entitlements](plans-and-entitlements.md)
- [Organizations and tenancy](organizations-and-tenancy.md)
- [Background processing](background-processing.md)
- [Add a metered feature](../getting-started/05-add-a-metered-feature.md)

