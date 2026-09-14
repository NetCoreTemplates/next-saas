# Architecture

Next SaaS is a single-runtime SaaS application. ASP.NET Core and ServiceStack own authentication, APIs, persistence, billing projection, background work, and production hosting. Next.js supplies an exported React client rather than a second production server.

[Documentation home](../README.md) · [Getting started](../getting-started/01-overview.md)

## Runtime shape

```text
Browser or API client
        │
        ▼
ASP.NET Core + ServiceStack
  ├── ASP.NET Core Identity sessions
  ├── organization-scoped API keys
  ├── typed APIs and authorization policy
  ├── plans, entitlements, quotas, and usage
  ├── local Stripe subscription projection
  ├── Background Jobs and operational queues
  └── static Next.js files in production
        │
        ├── RDBMS
        ├── file/object storage
        ├── Stripe
        └── SMTP
```

In Development, ASP.NET Core starts and proxies the Next.js development server for HMR. In Production, `next build` creates static files that are copied into `wwwroot`. Identity cookies and APIs therefore retain one public origin in both environments.

## Request path

A normal customer request passes through these boundaries:

1. ASP.NET Core Identity or a ServiceStack API key authenticates the caller.
2. `IWorkspaceContextResolver` reloads active membership and establishes the organization.
3. `IEntitlementResolver` decides whether the effective plan permits the feature.
4. `ISaasManager` evaluates local subscription access and quota state.
5. The service reads or changes only records belonging to the verified organization.
6. The service appends a registered, redacted audit event where appropriate.
7. Retryable, slow, or destructive work is persisted through ServiceStack Background Jobs.

The browser may select a resource or organization, but its identifier is never proof of access.

## Sources of truth

| Source | Owns |
| --- | --- |
| Stripe | Payment methods, charges, tax, invoices, discounts, and the remote subscription |
| RDBMS | Organizations, membership, plan versions, local subscription projection, entitlements, usage, product records, audit, and lifecycle state |
| JSON configuration | Deployment-wide product identity, supported feature/meter registry, storage limits, notification switches, retention defaults, and safety policy |
| Secret store | Database, Stripe, SMTP, and deployment credentials |
| File store | Opaque customer file and export objects; the RDBMS retains metadata and authorization state |

Stripe is not queried on an authorization or quota hot path. Signed webhooks and explicit reconciliation update the local `BillingSubscription`, which is used to determine access.

## Project boundaries

| Project | Purpose |
| --- | --- |
| `MyApp.ServiceModel` | Authoritative domain records and ServiceStack request/response contracts |
| `MyApp.ServiceInterface` | Services, policy, provider abstractions, telemetry, and job commands |
| `MyApp` | Host composition, Identity, plugins, migrations, configuration, health, and static assets |
| `MyApp.Client` | Static Next.js application and generated TypeScript client |
| `MyApp.Tests` | Unit, architecture, security, isolation, migration, and optional integration tests |

Dependencies point inward through contracts and interfaces. Product services should not acquire frontend dependencies or directly embed provider-specific policy.

## Important extension interfaces

- `IWorkspaceContextResolver` — resolves verified organization membership.
- `IEntitlementResolver` — computes effective feature access.
- `ISaasManager` — manages plans, access modes, usage, quotas, reservations, and organizations.
- `IFileStore` — streams opaque file objects independent of a storage vendor.
- `INotificationManager` — creates deduplicated notification intents.
- `IStripeBillingGateway` — isolates Stripe catalog, checkout, and reconciliation behavior.

Replace implementations during host composition instead of coupling feature services to a provider SDK. For example, an S3-backed `IFileStore` should preserve the same opaque keys, streaming, checksum, bounded-write, and idempotent-delete guarantees as `LocalFileStore`.

## Persistence model

The application uses EF Core for ASP.NET Core Identity and OrmLite for SaaS records. Both point to the same configured database. SQLite supports local development and deterministic verification; PostgreSQL is the default production policy.

Every OrmLite table is classified in `features.json`:

- **global** — shared plan catalog definitions;
- **tenant-owned** — data constrained to one organization;
- **platform-operational** — inboxes, deliveries, logs, snapshots, and run history.

An architecture test rejects unclassified tables. This makes ownership explicit to both maintainers and coding agents.

## API and generated-code boundary

C# DTOs in `MyApp.ServiceModel` are authoritative. ServiceStack generates `MyApp.Client/lib/dtos.ts`, which the React client calls through a shared `JsonServiceClient`.

```text
C# request/response DTO
        ↓
Service implementation
        ↓
ServiceStack metadata/OpenAPI
        ↓
generated TypeScript DTO
        ↓
React page or component
```

Regenerate after a contract change; never maintain parallel handwritten browser types.

## Asynchronous boundary

HTTP requests perform bounded work. Operations requiring durable retries or delayed execution persist their intent first and enqueue a command. Workers reload current database state rather than trusting serialized customer objects.

Examples include webhook processing, file deletion, export creation, notification delivery, subscription reconciliation, retention, rollups, and delayed organization deletion.

## Failure model

- External deliveries use stable idempotency or deduplication identifiers.
- Queue state and last errors remain inspectable to authorized operators.
- Destructive workflows are staged, audited, and safely replayable.
- Quota reservations prevent fallible work from oversubscribing capacity.
- Customer-visible errors include an `X-Request-Id` correlated with structured logs.
- Production startup is fail-closed when required infrastructure is missing.

## Architectural invariants

- Authentication is not tenant authorization.
- Organization context is resolved server-side for every customer operation.
- Published plan versions are immutable contracts.
- Customer exceptions are explicit, time-bound where appropriate, and audited.
- Stripe owns money; local state owns request-time access.
- Subscription state never triggers customer-data deletion.
- Usage and jobs are idempotent.
- Sensitive values are redacted before audit persistence.
- Dynamic application behavior is exposed through typed APIs, not a production Next.js server.

## Related concepts

- [Organizations and tenancy](organizations-and-tenancy.md)
- [Plans and entitlements](plans-and-entitlements.md)
- [Usage and quotas](usage-and-quotas.md)
- [Background processing](background-processing.md)

