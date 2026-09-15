# 3. Tour the project

The repository is organized so a feature has a predictable path from contract to implementation to UI.

[Previous: local setup](02-local-setup.md) · [Documentation home](../README.md) · [Next: customize](04-customize-the-product.md)

## Repository map

| Path | Responsibility |
| --- | --- |
| `MyApp.ServiceModel` | Authoritative SaaS entities and ServiceStack request/response DTOs |
| `MyApp.ServiceInterface` | Services, policy, billing gateway, storage, telemetry, and job commands |
| `MyApp` | ASP.NET Core host, plugins, Identity pages, migrations, configuration, and static assets |
| `MyApp.Client` | Next.js application, React components, styles, and generated DTO client |
| `MyApp.Tests` | Architecture, security, policy, Stripe, audit, and database tests |
| `scripts` | Development reset, diagnostics, verification, and production preflight |
| `config` | Deployment configuration |
| `features.json` | Machine-readable module ownership and table classification |
| `docs` | Developer onboarding and reference documentation |

![Repository Structure and Project Landmarks](../assets/project-map.png)

## Trace a typical API

`RecordUsage` demonstrates the normal flow:

1. `MyApp.ServiceModel/Saas.cs` defines the typed request and response.
2. `MyApp.ServiceInterface/SaasServices.cs` implements authorization and behavior.
3. `MyApp.Client/lib/dtos.ts` contains the generated TypeScript request class.
4. A React page imports that class and calls it through `MyApp.Client/lib/gateway.ts`.
5. Tests exercise quota, idempotency, and isolation behavior.

After changing a C# request or response contract, regenerate the browser client while the backend is running:

```bash
cd MyApp.Client
npm run dtos
```

Never make durable changes directly in `MyApp.Client/lib/dtos.ts`; regeneration replaces them.

![End-to-End Typed ServiceStack Client](../assets/typed-client.png)

## Backend landmarks

- `SaasServices.cs` contains customer-facing organization, plan, usage, member, and billing APIs.
- `SaasPlatformServices.cs` contains operator analytics, Customer 360, support access, audit, lifecycle, and retention administration.
- `FileStorageServices.cs` demonstrates safe quota reservation, streaming storage, export, and deletion jobs.
- `SaasMaintenanceJobs.cs` contains recurring operational commands.
- `SaasAudit.cs` centralizes registered audit actions and sensitive-value redaction.
- `AppConfig.cs` defines typed deployment-wide settings.
- `Configure.Saas.cs` registers SaaS dependencies and production configuration validation.

## Host landmarks

Configuration follows the `Configure.*.cs` convention:

- `Configure.Db.cs` selects SQLite or PostgreSQL.
- `Configure.Db.Migrations.cs` registers migration app tasks and empty-state bootstrap.
- `Configure.BackgroundJobs.cs` registers workers and recurring commands.
- `Configure.ApiKeys.cs` binds ServiceStack API keys to organizations.
- `Configure.HealthChecks.cs` defines `/up` and `/ready`.
- `Configure.Observability.cs` adds request correlation.
- `Configure.RequestLogs.cs` configures ServiceStack request logs.

`MyApp/Migrations/Migration1001.cs` creates the SaaS schema and seeds `plans.json`. Because this template is still under heavy development, a clean database is the expected way to apply structural changes here. A derived live product should use new numbered migrations.

## Frontend landmarks

![Customer Organization Navigation](../assets/customer-navigation.png)

- `app/page.tsx` and `app/(content)` provide the public site and Markdown-backed content routes.
- `app/dashboard`, `documents`, `usage`, `billing`, `team`, and `settings` are customer pages.
- `app/admin` contains the role-gated, static Operations Center routes.
- `components/app-shell.tsx` switches between organization navigation and capability-aware Operations Center navigation.
- `components/admin-center-page.tsx` composes the shared heading and focused content for each operator route.
- `components/saas-operations.tsx` owns the route-scoped Customer 360, analytics, queue, retention, support, and audit workflows.
- `lib/use-saas.tsx` loads and shares organization state without recursive requests.
- `lib/auth.tsx` supplies authentication and role guards.
- `styles/index.css` contains Tailwind and template-wide styling.

The production Next.js build is a static export. Pages fetch dynamic customer data from ServiceStack APIs in the browser; they do not require a Node.js server in production.

## Data ownership

`features.json` classifies every OrmLite table as one of:

- **global** — shared catalog definitions;
- **tenant-owned** — constrained to an organization;
- **platform-operational** — queues, logs, snapshots, and other operator state.

An architecture test fails when a new table is not classified. Update the relevant module, routes, configuration, jobs, and tests whenever a feature crosses those boundaries.

## Where a change belongs

| Change | Canonical location |
| --- | --- |
| Product name, contact details, retention default | `appsettings.json` or environment configuration |
| Initial clean-database plans | `MyApp/plans.json` |
| Runtime plan price, quota, trial, or feature | `/admin/plans` **Plans** tab, persisted in the RDBMS |
| Coupon or promotion code | `/admin/plans` **Coupons** tab, owned by Stripe and locally audited |
| Customer contract exception | `/admin/customers`, persisted with actor and reason |
| API contract | `MyApp.ServiceModel` |
| Business and authorization policy | `MyApp.ServiceInterface` |
| Customer or operator experience | `MyApp.Client` |
| Identity account page | `MyApp/Areas/Identity/Pages` |

[Next: customize the product →](04-customize-the-product.md)
