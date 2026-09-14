# Troubleshooting

Start with the customer-visible error, `X-Request-Id`, active organization, authenticated role, release revision, and UTC timestamp. Then correlate structured logs, audit events, product operations, and provider state.

[Operations](README.md) · [Observability and health](observability-and-health.md)

## Local diagnostics

```bash
./scripts/doctor.sh
dotnet build MyApp.slnx
dotnet test MyApp.slnx
cd MyApp.Client && npm run typecheck && npm run test:run && npm run build
```

If disposable SQLite state is inconsistent:

```bash
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes
```

This removes the documented local database and file store. Never use it against production or data that matters.

## Symptom guide

### Application will not start in Production

Read the aggregated `ProductionReadiness` errors. Common causes are localhost/example base URL, wildcard hosts, SQLite without `Deployment.RequirePostgreSql=false`, automatic migration without `Deployment.RequireExplicitMigrations=false`, Development/Disabled email, placeholder support address, missing Stripe/webhook values, or test keys without explicit staging allowance.

Run `./scripts/preflight.sh --config-only` with the same environment as the deployment.

### `/up` succeeds but `/ready` fails

The process is alive but the database query or file-store write/delete probe failed. Check connection/permissions, storage root/path ownership, disk capacity, network/DNS, and recent secret rotation. Do not route normal traffic until ready.

### Browser page flickers or repeatedly calls an API

Inspect React effect dependencies. Memoize loaders with `useCallback`, depend on stable scalar keys, guard duplicate/in-flight requests, and do not include freshly allocated request/response objects in dependencies. Add a test that asserts request count.

### Checkout returns success but the organization remains Free

Inspect `checkout=success`, Checkout Session metadata, `BillingSubscription`, webhook delivery/signature, `StripeEventInbox`, and the reconciliation job. Ensure all keys, Products, Prices, and Customers belong to the same Stripe test/live mode. Use confirmation/reconciliation rather than editing subscription rows.

### Trial asks for no card

This is expected when `Saas.TrialRequiresPaymentMethod=false`. Change the global policy if a payment method must be collected at trial start, then create a new Checkout Session.

### API key is missing or rejected

Confirm the user selected the organization where it was created, remains an active member, and has `api.access`. Lists are organization-filtered and raw keys are shown once. Check expiry/revocation, use `X-Api-Key` or `Bearer ak-...`, and observe `429 Retry-After` for rate limiting.

### Quota usage is incorrect

Search immutable `UsageEvent`, current period, aggregate/reserved units, effective plan version, customer overrides, and idempotency key. Check expired reservations and rollup freshness. Do not correct counters by direct SQL; only gauges have an audited operator adjustment.

### Notification is absent

Check user/template/channel preferences, deduplication key, `Notifications.Provider`, SMTP configuration, delivery status/attempts, and the provider. Development delivery stays local. Fix the cause before using the Admin retry action.

### Organization deletion is not progressing

Check scheduled time, organization status, paid subscription state, legal hold, lifecycle error, job health, and file-store access. A pending deletion intentionally makes the organization read-only during the delay. User accounts survive organization deletion.

### Retention is not deleting rows

Check the last `DataRetentionRun`, organization override, legal hold, eligible timestamps, terminal record statuses, batch limit, and recurring-job health. Backlogs may require multiple daily runs.

## Escalation record

Capture request ID, organization ID, affected user/operator, exact error code, release/config version, provider event IDs, actions taken, and whether customer data or access changed. Never paste secrets or raw API keys into tickets.

## Related documentation

- [Background jobs and recovery](background-jobs-and-recovery.md)
- [External services](external-services.md)
- [Support operations](../features/support-operations.md)
