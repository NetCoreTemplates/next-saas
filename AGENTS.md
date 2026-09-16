# Agent guide: Next SaaS

This repository is a full-stack .NET 10, ServiceStack, Next.js 16, React 19, TypeScript, OrmLite, ASP.NET Core Identity, and Stripe Billing template.

Read [PLAN.md](PLAN.md) before changing product policy. Read [README.md](README.md) for setup and operator workflows.

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
11. The database provider is a configuration choice, never a code fork. SQLite is the default.

## Database providers

`MyApp/Configure.Db.cs` branches on `Database:Provider` for both OrmLite and EF Core, so the
application itself is provider-agnostic. Deployment selects a provider with a Kamal destination:
`config/deploy.<provider>.yml` deep-merges over `config/deploy.yml`, secrets resolve from
`.kamal/secrets-common` plus `.kamal/secrets.<provider>`, and `config/db/<provider>/pre-deploy.sh`
provisions any accessory. The Release workflow passes `-d "$DB_PROVIDER"` to every `kamal`
command, where `DB_PROVIDER` is a repository variable defaulting to `sqlite`.

When adding a provider, add all of its files rather than branching the pipeline:

- do not add database accessories to `config/deploy.yml`, and do not add provider credentials to
  `.kamal/secrets-common`;
- a provider that runs a database server takes exactly one operator-managed secret, `DB_PASSWORD`;
  map it to whatever additional env vars the image requires inside `.kamal/secrets.<provider>`
  rather than adding another GitHub secret;
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
- customer-specific exceptions belong in `CustomerEntitlementOverride` with actor, reason, and validity window.

Effective entitlement precedence is customer override, pinned plan version, published Free plan, then global fallback.

Global JSON configuration is read-only in Admin UI. It should remain reviewable infrastructure configuration.

## Key files

```text
PLAN.md                                      product and architecture decisions
README.md                                    setup, Stripe, usage, and operations guide
MyApp.ServiceModel/Saas.cs                   domain entities and API contracts
MyApp.ServiceInterface/SaasServices.cs       workspace, usage, billing, and admin policy
MyApp/Configure.Saas.cs                      dependency setup and Stripe SDK gateway
MyApp/Migrations/Migration1001.cs            SaaS schema and default plan seed
MyApp/appsettings.json                       global SaaS and Stripe policy
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
```

## Common commands

From the repository root:

```bash
dotnet build MyApp.slnx
dotnet test MyApp.slnx
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

Regenerate DTOs after any `MyApp.ServiceModel` request/response change:

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

## Workspace and identity rules

- Every user receives a personal workspace lazily or at registration.
- A `WorkspaceMember` controls product access.
- Workspace roles are Owner, Admin, Billing, and Member.
- Only platform operators use the ASP.NET Identity `Admin` role.
- Do not place an API key into an interactive Identity session.
- API-key calls resolve the acting user, then the user's workspace membership.

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
7. update `README.md`, `PLAN.md`, or this guide if an invariant or workflow changed.
