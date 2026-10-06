# Agent guide: Next SaaS

This repository is a full-stack .NET 10, ServiceStack, Next.js 16, React 19, TypeScript, OrmLite, ASP.NET Core Identity, and Stripe Billing template.

Read [PLAN.md](PLAN.md) before changing product policy. Read [README.md](README.md) for setup and operator workflows, and the [database guide](https://react-templates.net/docs/next-saas/getting-started/choose-your-database) for the provider workflow that spans local development and production.

The canonical product documentation is the published docs hub, whose source lives in a separate
repository. Keep it in sync with every change; see [Documentation](#documentation).

## Product boundary

This template is for subscription SaaS and API products. It includes workspaces, memberships, plans, quotas, usage, Stripe subscriptions, API keys, customer self-service, and administration.

It is not a physical-product commerce template. Do not add carts, inventory, shipping, fulfillment, marketplace payouts, or Stripe Connect unless a derived product explicitly needs them.

The visible `Acme` product is reference branding for a hosted document-storage, grounded AI, search, and analytics service. `MyApp` namespaces remain intentionally generic for template generation.

Use **Organization** in all customer-facing UI and copy. The internal domain, database, DTO, and service names intentionally remain `Workspace` for template compatibility.

## Architectural invariants

1. ASP.NET Core is the only production runtime.
2. Next.js produces a static export. Do not add request-time React Server Components, Route Handlers, or Server Actions.
3. All dynamic behavior is a typed ServiceStack API.
4. C# request and response DTOs are authoritative. Regenerate `MyApp.Client/lib/dtos.ts` after contract changes.
5. Stripe owns payment collection, invoices, credits, discounts, taxes, Products, Prices, Customers, and Subscriptions.
6. The application owns workspaces, plan mappings, entitlements, quotas, the enforcement ledger, customer overrides, and the local Stripe projection.
7. Never query Stripe on a normal API authorization or quota path.
8. Never put Stripe secret or webhook keys in browser code.
9. Published plan versions are immutable. Create a draft version for changes.
10. Customer deletion or payment failure never deletes product data; it changes access policy.
11. The database provider is a configuration choice, never a code fork. SQLite is the default,
    and whichever provider is deployed is also the one run locally.
12. Tenant isolation is enforced by the database connection and fails closed. Tables owned by an
    organization implement `IHasWorkspaceId`. APIs for an organization say which one with the
    `WorkspaceId` of their Request DTO (`IRequireWorkspace`). Connections opened for a request cannot use
    tenant-owned tables until the user is checked to be a member of it, and are then confined to it. Code that works
    across organizations opts out explicitly with `AcrossWorkspaces()`, or opens its connection with
    `dbFactory.OpenAcrossWorkspaces(userId)`, in a file on the allow-list in `ArchitectureGuardTests`.
13. Audit columns are set by the connection, not by application code. Tables with audit columns derive
    from `SaasAuditBase`; never assign `CreatedDate`, `CreatedBy`, `ModifiedDate`, or `ModifiedBy`.

## Database providers

`MyApp/Configure.Db.cs` branches on `Database:Provider` for both OrmLite and EF Core, so the
application itself is provider-agnostic. `DB_PROVIDER` in `.env` selects the provider on both
sides, and it is the only place a provider is named: `Program.cs` applies it as
`Database:Provider` when that setting is absent, so `.env` never restates the provider. An
explicit `Database__Provider` still wins, which is how a destination named after something
other than a provider, such as a managed instance, names its engine.
`scripts/dev-db.sh` runs the provider locally from the same image, database name, login, and
initializer scripts the deployment uses, writing the connection into the private, gitignored
`.env` that `Program.cs` applies in Development before `CreateBuilder` runs, because
`CreateBuilder` composes HostingStartup configuration. Local overrides belong in `.env`, never in
a source-controlled settings file: `MyApp/appsettings.Development.json` stays on the SQLite
default a new clone starts from, and `MyApp/appsettings.json` stays at the template default.
Deployment selects a provider with a Kamal destination:
`config/deploy.<provider>.yml` deep-merges over `config/deploy.yml`, secrets resolve from
`.kamal/secrets-common` plus `.kamal/secrets.<provider>`, and `config/db/<provider>/pre-deploy.sh`
provisions any accessory. The Release workflow passes `-d "$DB_PROVIDER"` to every `kamal`
command, where `DB_PROVIDER` is a repository variable defaulting to `sqlite`.

When adding a provider, add all of its files rather than branching the pipeline:

- add its local case to `scripts/dev-db.sh` in the same change as its deployment files, so
  developers are never pushed back onto a different engine than production, and add it to the
  provider table in the published [database guide](https://react-templates.net/docs/next-saas/getting-started/choose-your-database);
- do not add database accessories to `config/deploy.yml`, and do not add provider credentials to
  `.kamal/secrets-common`;
- a provider that runs a database server takes exactly one operator-managed secret, `DB_PASSWORD`;
  map it to whatever additional env vars the image requires inside `.kamal/secrets.<provider>`
  rather than adding another GitHub secret;
- only SQLite has checked-in EF Core migrations; every server provider bootstraps Identity from
  the current model, so `Configure.Db.Migrations.cs` creates that schema explicitly when
  `AspNetUsers` is absent rather than calling `EnsureCreated()`;
- the operator scripts load `.env` with Dotenv semantics, where an existing environment variable
  always wins; never source it with `set -a`, which silently overrides explicit arguments;
- do not add provider-specific steps to `.github/workflows/release.yml`; use the pre-deploy hook;
- every Kamal invocation in the workflow must carry `-d "$DB_PROVIDER"`, because a destination
  also selects the secrets file;
- a destination scopes container names (`<service>-web-<destination>-<version>`) and the
  `destination=` label that every Kamal removal filters on, so commands run under one destination
  cannot see deployments made under another, or under none;
- Kamal requires `config/deploy.<provider>.yml` to exist for any destination it is given and to
  parse as a YAML mapping — a comments-only file loads as `false` and raises `symbolize_keys` —
  and its deep merge replaces arrays rather than appending to them;
- `Deployment.RequireNetworkDatabase` is the production policy gate; it rejects Sqlite for any
  provider rather than naming one.

## Configuration ownership

Keep configuration in the correct scope:

- deployment-wide behavior belongs under `Saas` or `Stripe` in `MyApp/appsettings.json` and environment overrides;
- the database provider belongs in `Database:Provider` plus a Kamal destination (see below);
- plan versions, features, quota amounts, display order, and Stripe Price mappings belong in the RDBMS;
- customer-specific exceptions belong in `CustomerEntitlementOverride` with user, reason, and validity window.

Effective entitlement precedence is customer override, pinned plan version, published Free plan, then global fallback.

Global JSON configuration is read-only in Admin UI. It should remain reviewable infrastructure configuration.

## Documentation

The docs hub at `https://react-templates.net/docs/next-saas` is the canonical documentation, and
its source is a sibling checkout:

```text
../../ServiceStack/react-templates.net/content/docs/next-saas/    relative to this repository
```

If that checkout is missing, say so and list the pages that need updating instead of silently
leaving them stale.

Treat those pages as part of the change, not as follow-up work. A behavior change that a page
describes is not complete until the page describes the new behavior.

| What changed | Pages to check |
| --- | --- |
| database provider, `.env`, local setup | `getting-started/choose-your-database`, `getting-started/local-setup`, `operations/choose-a-database`, `operations/configuration`, `operations/database-and-storage` |
| an operator script's name, flags, or output | the page that prints that command, plus `operations/*` |
| a customer-facing page or flow | the matching `features/*` page, and `getting-started/project-tour` |
| an API contract or DTO | `features/*` for that module, and `development/*` recipes that call it |
| quota, entitlement, or billing policy | `concepts/usage-and-quotas`, `concepts/plans-and-entitlements`, `features/billing-and-subscriptions` |
| a security control or production gate | `security/*`, `operations/configuration` |
| a new onboarding step | `getting-started/meta.json` and the numbered list in `index.mdx`, plus the same list in `README.md` |

Rules for those edits:

- state current behavior; do not narrate the change or reference a version that shipped it;
- grep the whole `next-saas` docs tree for the old claim rather than editing only the obvious
  page, because the same fact is often stated in getting-started, features, and operations;
- keep `README.md` and this guide consistent with the hub; the repository files are the short
  version and must not contradict it;
- run `npm run build` in the docs repository, which also catches a broken `meta.json`;
- check that every internal `/docs/...` link and `#anchor` you add resolves.

## Key files

```text
PLAN.md                                      product and architecture decisions
README.md                                    setup, Stripe, usage, and operations guide
MyApp.ServiceModel/Saas.cs                   domain entities and API contracts
MyApp.ServiceInterface/SaasServices.cs       workspace, usage, billing, and admin policy
MyApp.ServiceInterface/SaasDb.cs             connection rules: tenant confinement and audit columns
MyApp.ServiceInterface/WorkspaceData.cs      what an organization owns: export, deletion, stored files
MyApp/Configure.Saas.cs                      dependency setup and Stripe SDK gateway
MyApp/Migrations/Migration1000.cs            SaaS schema and default plan seed
MyApp/appsettings.json                       global SaaS and Stripe policy
MyApp/appsettings.Development.json           SQLite development default; .env overrides it
scripts/dev-db.sh                            runs the deployed database provider locally
MyApp/Configure.ApiKeys.cs                   API key scopes
MyApp/Configure.BackgroundJobs.cs            job infrastructure
MyApp/Configure.RequestLogs.cs                diagnostic request logging
MyApp/Configure.Security.cs                   security headers, same-origin policy, auth throttling
MyApp.Client/lib/dtos.ts                     generated TypeScript client; do not hand edit
MyApp.Client/components/app-shell.tsx        authenticated application shell
MyApp.Client/styles/index.css                design tokens and global styles
MyApp.Client/app/admin/*/page.tsx             Operations Center static routes
MyApp.Client/components/admin-center-page.tsx shared operator route content
MyApp.Tests/SaasManagerTests.cs               quota and idempotency policy tests
MyApp.Tests/TenantIsolationTests.cs           every tenant-owned table is confined to its organization
MyApp.Tests/SaasAuditRuleTests.cs             audit columns are set by the connection
MyApp.Tests/WorkspaceAccessTests.cs           availability by organization state
MyApp.Tests/WorkspaceDataTests.cs             export, deletion, confined jobs, stored files
MyApp.Tests/MaintenanceJobTests.cs            retention and rollups per organization
```

## Common commands

From the repository root:

```bash
dotnet build MyApp.slnx
dotnet test MyApp.slnx
```

Run the deployed database locally before starting the application:

```bash
./scripts/dev-db.sh up        # provider from DB_PROVIDER; sqlite needs no container
./scripts/dev-db.sh status
```

Run the application:

```bash
cd MyApp
dotnet watch
```

ASP.NET Core listens on `https://localhost:5001` and starts/proxies the Next.js development server.

Frontend validation:

```bash
cd MyApp.Client
npm run typecheck
npm run test:run
npm run build
```

DTOs are regenerated each time the App starts in Development (`Configure.StartupTasks.GenerateDtos.cs`), for
`dtos.*` files whose `BaseUrl` is one of the App's URLs. An App started on another URL, e.g. with `--urls`, skips
them. To regenerate them yourself after a `MyApp.ServiceModel` request/response change:

```bash
cd MyApp.Client
npm run dtos
```

Run database migrations:

```bash
cd MyApp
npm run migrate
```

Create a new OrmLite migration instead of editing an already shipped migration in a derived production application.
Migrations declare their own copies of the tables they create or change, as they are at that migration, never the
App's models in `MyApp.ServiceModel`, which are the latest version of each table. `MigrationSchemaTests` checks that
running every migration creates the tables of the App's models.

## Backend conventions

Service contracts live in `MyApp.ServiceModel`. Implement services in `MyApp.ServiceInterface`. Host/integration concerns such as the Stripe SDK live in `MyApp` behind interfaces defined by the service layer.

Use explicit routes and marker interfaces:

```csharp
[ValidateIsAuthenticated]
[Route("/saas/widgets", "POST")]
public class CreateWidget : IPost, IReturn<CreateWidgetResponse>
{
    [ValidateNotEmpty]
    public string Name { get; set; } = "";
}
```

Prefer declarative validation attributes. Enforce workspace membership and role inside the service because ASP.NET Identity roles and workspace roles are separate concepts.

Use `DateTime.UtcNow` for persisted policy timestamps. Public identifiers should be opaque strings/UUIDs. Add unique constraints for idempotency and business invariants.

Use OrmLite for product data and EF Core only for ASP.NET Identity data. Use AutoQuery for conventional administrator-facing CRUD when it does not bypass a domain invariant.

### Database connections

`MyApp.ServiceInterface/SaasDb.cs` declares the OrmLite connection filters and write rules every connection uses, in `FilterSet`s, so that tenant isolation and audit columns don't depend on each query and write remembering them.

| Connection | Tenant-owned tables | Audit columns |
| --- | --- | --- |
| `Db` in a service, AutoQuery | Throw unless the Request DTO implements `IRequireWorkspace`, then confined to its organization | The signed-in user |
| `Db.AcrossWorkspaces()` | Not confined | The same user id as `Db` |
| `dbFactory.OpenForWorkspace(id, "job-name")` in a job or command | Confined to that organization | The name it's given |
| `dbFactory.OpenAcrossWorkspaces("job-name")` in a job, command, or request filter | Not confined; keep explicit `WorkspaceId` conditions. Limited to the allow-list in `ArchitectureGuardTests` | The name it's given |
| `dbFactory.Open()` | Not allowed outside migrations and the health check, which `ArchitectureGuardTests` enforces | |

Rules for new code:

- Implement `IRequireWorkspace` in the Request DTO of every API for an organization. `SaasDb.ForRequest()` reads it from `IRequest.Dto` when the request's connection opens, checks the user is a member of its `WorkspaceId` and confines the request; services read the organization and membership with `Db.GetWorkspaceScope()`. Never look up the organization or membership in a service.
- `GET` APIs need `Read` access and others need `Write`. Add `[WorkspaceAccess(WorkspaceAccess.Account)]` to APIs that administer the organization itself. `Account` stays available to a suspended or read-only organization, `Read` is blocked while suspended, and `Write` is also blocked while read-only or pending deletion. Never check organization status in a service.
- In the client pass the tab's organization with each call: `new QueryStoredFiles({ workspaceId: tabWorkspaceId() })`.
- Then query tenant-owned tables without a `WorkspaceId` condition, look rows up with `Db.SingleById<T>(id)`, and insert them without a `WorkspaceId`. Writing the condition anyway would hide a connection that isn't confined, which instead fails closed, so `ArchitectureGuardTests` only allows it in files that work across organizations. Read the organization's subscription with `Db.GetSubscription()`.
- Code that's given a connection and relies on it being confined, e.g. `SaasManager`'s usage methods, the entitlement resolver, notifications and the Stripe gateway, calls `db.AssertConfinedTo(workspace.Id)` first, so it can't work on every organization's rows when it's given a connection that isn't.
- AutoQuery APIs over tenant-owned tables implement `IRequireWorkspace` too.
- Platform APIs for one customer call `RequireCustomer(workspaceId)`, which confines the request to that customer. Only listings across customers use `PlatformDb`.
- Never give a table a nullable `WorkspaceId`. If some of its rows aren't about an organization, split it, as the audit log is: `SaasAuditEvent` for an organization's events and `PlatformAuditEvent` for the platform's (plans, coupons, Stripe events).
- Keep the explicit organization condition in raw SQL and in jobs that sweep across organizations. Write raw SQL with `Sql.Fmt()`, e.g. `db.ExecuteSql(Sql.Fmt($"UPDATE {Table} SET {Column} = {value} WHERE {WorkspaceId} = {workspaceId}"))`, which `ArchitectureGuardTests` enforces.
- Work for one organization carries its `WorkspaceId` in the job payload, and the command opens `dbFactory.OpenForWorkspace(workspaceId, "name")` before reading anything. Never open a connection with `dbFactory.Open()`.
- A job that sweeps across organizations finds its work with `dbFactory.OpenAcrossWorkspaces("name")`, then does each organization's work on its own `dbFactory.OpenForWorkspace(workspaceId, "name")` connection, as the Stripe, reconciliation and quota notification jobs do.
- Use `files.ForWorkspace(workspaceId)` for stored objects, never `IFileStore` directly.
- Filter, sort, count and page in the database with `Db.From<T>()`, not on a selected list. Compare text with `ToLower()` so searches behave the same on every provider.
- Queries that every request or metered API runs are compiled with `OrmLiteQuery.Compile()` in `SaasQueries`, so their SQL is generated once and shared by every organization. `CompiledQueryTests` checks they and `SaasDb.WorkspaceFilters` reuse their SQL.
- `Insert` and `Update` leave the row with the `WorkspaceId` and audit columns the connection wrote, so it can be returned or used without reading it back.
- Give background code a name as its user id, and use `using (db.WithUserId("policy-name"))` for a system policy applied during a user's request.
- Data seeded with its own audit dates uses `WithoutFilters()`, as `ExampleDataSeeder` does.
- A new tenant-owned table implements `IHasWorkspaceId`, derives from `SaasAuditBase`, has an index or unique constraint starting with `WorkspaceId`, and is classified `tenantOwned` in `features.json`. It's then isolated, exported and deleted with its organization without further changes; add it to `WorkspaceData.NotExported` or `KeptAfterDeletion` with a reason to opt out.

The docs hub explains each rule and the test that protects it: [Tenant isolation](https://react-templates.net/docs/next-saas/security/tenant-isolation) and [Organizations and tenancy](https://react-templates.net/docs/next-saas/concepts/organizations-and-tenancy).

## Workspace and identity rules

- Every user receives a personal workspace lazily or at registration.
- A `WorkspaceMember` controls product access.
- Workspace roles are Owner, Admin, Billing, and Member.
- Only platform operators use the ASP.NET Identity `Admin` role.
- Do not place an API key into an interactive Identity session.
- A request sent with an API key is authenticated as the key's user without their roles (`AddApiKeyAuth()`), and works in the organization the key is bound to. It can only call APIs that have `[ValidateHasScope("scope")]`, with a key that has that scope. Signed-in users have every scope in `SaasApiKeys.Scopes` as claims, so the same attribute passes for them. Add the attribute, and the API to the allow-list in `ArchitectureGuardTests`, only for APIs meant for programmatic use.
- API-key calls act as the key's user, in the organization the key was created for, after checking the user is still a member of it.

## Usage and quota rules

The immutable `UsageEvent` ledger is the source of truth. `UsageAggregate` is a transactional performance projection.

Every usage write requires:

- workspace;
- meter key;
- positive units;
- an idempotency key unique within the workspace;
- a current usage period;
- the resolved effective allowance and enforcement mode.

Hard limits reject the entire operation before writing a usage event. Replayed idempotency keys return the already accepted result and never consume twice.

Free periods are calendar months in UTC. Paid periods follow the locally projected Stripe subscription boundaries.

Request logs are diagnostics, not billing data. Rate limits protect short-term capacity, not commercial quotas.

For a multi-step operation that can fail after admission, reserve units first, then finalize or release them. `UsageAggregate.ReservedUnits` exists for this extension.

Always add tests for:

- first usage acceptance;
- idempotent replay;
- exact-limit acceptance;
- over-limit rejection;
- override precedence;
- period rollover;
- concurrent writers when changing the aggregate algorithm.

## Stripe rules

Use the official Stripe .NET SDK through `IStripeBillingGateway`.

- Checkout and Customer Portal are hosted by Stripe.
- Validate the raw webhook body and `Stripe-Signature` before parsing.
- Insert a unique `StripeEventInbox` row before acknowledging a new event.
- Process the inbox through `ProcessStripeEventCommand` in ServiceStack Background Jobs.
- Make event handling replay-safe and tolerant of out-of-order delivery.
- Store only Stripe object identifiers and the local projection needed for authorization.
- Do not store card or bank details.

The checked-in application must remain useful without Stripe credentials. Free works; paid actions return descriptive configuration errors.

Use Stripe-hosted invoice PDFs. Add `ServiceStack.Pdf` only when the product specifically requires a separate branded statement.

## Frontend conventions

The design thesis is “precision enterprise ledger”:

- midnight navy operational surfaces;
- porcelain content surfaces;
- cobalt primary actions;
- mint healthy-state signals;
- restrained 10–16px corner radii;
- dense but breathable information hierarchy;
- system font stack and no runtime font dependency.

Reuse `AppShell`, `PageHeading`, `Panel`, and `StatusPill`. Use Lucide icons for functional UI. Keep marketing pages in `Layout`; keep authenticated product pages in `AppShell` and wrap them with `ValidateAuth`.

Fetch APIs with generated request classes:

```tsx
const client = useClient()
const api = await client.api(new RecordUsage({
  meterKey: 'api.requests',
  units: 1,
  idempotencyKey: crypto.randomUUID(),
}))
```

Never duplicate secret-dependent business logic in the browser. It is acceptable for public pricing to include a matching static fallback so the static page remains meaningful while the API is unavailable; the API response replaces it when connected.

All routes must remain static-export compatible. Verify with `npm run build`.

## Administration

`/admin` is the product-specific Operations Center overview. Focused static routes live under `/admin/*`; `/admin/plans` uses separate Plans and Coupons tabs. `/admin-ui` supplies ServiceStack user, jobs, request-log, and database administration.

Plan metadata and customer overrides are RDBMS-managed. Changes that alter customer contracts should use a draft-and-publish workflow. Do not mutate historical usage periods or published plan versions to “fix” a customer; create an audited override or migration.

Keep webhook bodies out of request logs. Keep secrets and raw API keys out of logs and audit detail JSON.

The Content Security Policy withholds `'unsafe-eval'` from customer-facing routes. ServiceStack's built-in operator UIs need it because they compile Vue templates at runtime, so `Security.ToolingPaths` scopes a derived relaxed policy to those paths. Never add `'unsafe-eval'` to `Security.ContentSecurityPolicy` itself.

Cookie-authenticated mutations under `/saas` require a same-origin `Origin` or `Referer`; API-key calls are exempt from browser CSRF checks and remain credential-, membership-, feature-, quota-, and rate-limit protected. Sensitive authentication POST routes use the ASP.NET rate limiter. Keep both controls when adding authentication or customer mutation routes.

Personal account deletion is separate from organization deletion. Owners must transfer or delete owned organizations first. Account deletion must revoke organization memberships, API keys, notifications, active support grants, and the active-workspace preference before removing Identity.

## Definition of done

Before completing a change:

1. run `dotnet build MyApp.slnx`;
2. run `dotnet test MyApp.slnx`;
3. regenerate DTOs if contracts changed;
4. run frontend type-check and tests;
5. run the production static export;
6. verify no secret, generated database, or local data-protection key is staged;
7. update `README.md`, `PLAN.md`, or this guide if an invariant or workflow changed;
8. update the docs hub pages the change affects and build that repository (see
   [Documentation](#documentation)).
