# Next SaaS

A production-oriented .NET 10 + ServiceStack + Next.js 16 template for self-serve individual and business SaaS products

The included Acme product is a deliberately small hosted document-storage and analytics service. Its file module exists to demonstrate tenant isolation, quota reservations, exact storage gauges, analytics, and lifecycle operations—not document parsing, ingestion, search, or RAG.

Registration creates either a private **Individual** account or a team-ready **Business** organization. Internal code, DTOs, and database records retain the `Workspace` name for both tenant boundaries.

## What is included

- enterprise marketing site, pricing, sign-in, and registration;
- individual accounts and business organizations with invitation acceptance, persisted workspace switching, and Owner, Admin, Billing, and Member roles for businesses;
- shared Free, Individual-only Personal, Business-only Pro and Business, and sales-led Enterprise tiers;
- immutable published plan versions, features, prices, and quotas;
- Stripe Checkout, Customer Portal, plan trials, admin-managed promotion codes, signed webhook validation, and local subscription projection;
- immutable, idempotent document-count, exact-byte storage, upload, API-request, and seat usage with real-time quota aggregates and hard-limit enforcement;
- ServiceStack API Keys with workspace and usage scopes;
- customer overview, documents, usage analytics, billing, API keys, team, audit, notifications, and lifecycle settings;
- role-gated SaaS analytics, customer 360, support/operations tooling, plus ServiceStack Admin UI and Admin Database;
- customer-specific entitlement overrides with business reason and audit actor;
- Background Jobs for Stripe webhooks, file deletion, rollups, snapshots, notifications, exports, and staged workspace deletion;
- request logging, profiling, OpenAPI/Scalar, liveness/readiness health checks, and generated TypeScript DTOs;
- static Next.js production output served by the single ASP.NET Core runtime.

## Documentation

> **Start with the [Next SaaS documentation](https://react-templates.net/docs/next-saas).** This README is a quick reference. The published guides explain how to use, customize, and ship the template, step by step.

New to the template? Work through the onboarding guides in order:

1. [Understand the template](https://react-templates.net/docs/next-saas/getting-started/overview)
2. [Choose your database](https://react-templates.net/docs/next-saas/getting-started/choose-your-database)
3. [Run it locally](https://react-templates.net/docs/next-saas/getting-started/local-setup)
4. [Tour the project](https://react-templates.net/docs/next-saas/getting-started/project-tour)
5. [Customize the product](https://react-templates.net/docs/next-saas/getting-started/customize-the-product)
6. [Add a metered feature](https://react-templates.net/docs/next-saas/getting-started/add-a-metered-feature)
7. [Connect Stripe sandbox](https://react-templates.net/docs/next-saas/getting-started/connect-stripe-sandbox)
8. [Verify and ship](https://react-templates.net/docs/next-saas/getting-started/verify-and-ship)

Then use the reference guides, which you can read in any order:

| Section | What it covers |
| --- | --- |
| [Concepts](https://react-templates.net/docs/next-saas/concepts/architecture) | Architecture, organizations and tenancy, plans and entitlements, usage and quotas, background processing |
| [Features](https://react-templates.net/docs/next-saas#reference-documentation) | Billing, plans and trials, coupons, members, API keys, file storage, analytics, notifications, audit logs, data lifecycle, Operations Center |
| [Development](https://react-templates.net/docs/next-saas/development) | Recipes for APIs, feature gates, meters, background jobs, migrations, frontend pages, DTO generation, testing, AI-assisted development |
| [Operations](https://react-templates.net/docs/next-saas/operations) | Configuration, secrets, deployment, database and storage, health, job recovery, retention, backup and restore, troubleshooting |
| [Security](https://react-templates.net/docs/next-saas/security) | Authentication, authorization, tenant isolation, API credentials, webhook security, support access, privacy, production security review |

Two guides in this repository cover the first decisions you make, in order:

1. [DATABASE.md](DATABASE.md) — choose your database provider and configure it for local
   development and production at the same time. Do this first. Published version:
   [Choose your database](https://react-templates.net/docs/next-saas/getting-started/choose-your-database).
2. [CUSTOMIZE.md](CUSTOMIZE.md) — product identity, plans, meters, and content.

The architectural and product decisions are recorded in [PLAN.md](PLAN.md), and [features.json](features.json) is a machine-readable map of the modules.

## Run locally

### Step 1 — Install dependencies

```bash
cd MyApp.Client
npm install
cd ../MyApp
npm install
```

### Step 2 — Choose your database

This is the first thing to customize, and it is one decision for both environments:
`DB_PROVIDER` selects the database you run locally, the `Database:Provider` the application
uses, *and* the Kamal destination that deploys.

```bash
cp .env.example .env                 # first time only
```

Set the provider in `.env` — `sqlite`, `postgres`, `mysql`, or `sqlserver`:

```bash
DB_PROVIDER=postgres
```

Start it:

```bash
./scripts/dev-db.sh up
```

For a server provider this runs the same image as the deployment's accessory, with the same
`next_saas` database, the same unprivileged login, and the same initializer scripts, then writes
the connection into your private `.env`, which the application reads in Development. SQLite starts no container and stays the zero-dependency default.

**[DATABASE.md](DATABASE.md) is the step-by-step guide**, and it continues past local setup into
configuring the same provider for production.

### Step 3 — Create the schema

```bash
cd MyApp
npm run migrate
```

The first normal application start also detects an empty database and creates the Identity schema, SaaS schema, reference plans, and Development-only sample users automatically. Existing databases are never destructively recreated; use the explicit migration task when applying later migrations.

### Step 4 — Start the application

Start the ASP.NET Core host. It automatically starts and proxies the Next.js development server:

```bash
cd MyApp
dotnet watch
```

Open `https://localhost:5001`. Seeded development accounts all use `p@55wOrd`:

- `admin@email.com` — platform administration;
- `manager@email.com` — standard customer flow;
- `employee@email.com` — standard customer flow;
- `test@email.com` — minimal authenticated account.

Then customize the product itself: [CUSTOMIZE.md](CUSTOMIZE.md).

## Runtime architecture

In development, ASP.NET Core owns the public origin and proxies page/HMR traffic to Next.js. In production, `next build` creates a static export and ASP.NET Core serves it directly from `wwwroot`; no Node.js process or Next.js server is required.

Dynamic behavior always goes through typed ServiceStack APIs:

```text
Browser / API client
        │
        ▼
ASP.NET Core + ServiceStack
  ├── Identity sessions and API keys
  ├── workspaces, plans, entitlements
  ├── local usage ledger and quota checks
  ├── Stripe billing gateway
  └── Background Jobs webhook worker
        │
        ├── RDBMS (enforcement truth)
        └── Stripe (payments and invoices)
```

Stripe is never queried on the normal authorization path. Signed webhooks update a local projection asynchronously.

## Configuration ownership

Configuration has deliberate boundaries:

- deployment-wide policy is source-controlled in `MyApp/appsettings.json` under `Saas` and `Stripe`;
- plans, versions, prices, features, and quotas are RDBMS records managed by administrators;
- negotiated customer exceptions are audited RDBMS overrides.

Effective access uses this precedence:

1. active customer override;
2. workspace subscription's pinned plan version;
3. published Free plan;
4. deployment-wide fallback.

Checked-in Stripe secrets are empty. Supply secrets through environment variables or `APPSETTINGS_PATCH`:

```bash
export Stripe__PublishableKey=pk_test_...
export Stripe__SecretKey=sk_test_...
export Stripe__WebhookSecret=whsec_...
```

Production startup is intentionally fail-closed. `Deployment` policy checks reject placeholder URLs and support addresses, wildcard hosts, a file-based database, development email delivery, automatic first-request migration, missing Stripe/webhook credentials, and test Stripe keys. Each requirement is configurable for staging or an intentionally free-only product, but weakening one requires an explicit setting. Development startup is unaffected.

Global policy is read-only in the browser so production changes remain reviewable. The `/admin` Operations Center groups customer, plan, usage, operational, security, and settings workflows into static routes, with low-level data access available to authorized operators at `/admin-ui/database`.

## Stripe setup

For a complete walkthrough, see [Connect Stripe sandbox](https://react-templates.net/docs/next-saas/getting-started/connect-stripe-sandbox) and [Billing and subscriptions](https://react-templates.net/docs/next-saas/features/billing-and-subscriptions).

1. Set `Stripe__SecretKey` to a Stripe sandbox key and restart the application.
2. Sign in as an administrator, open `/admin/plans`, remain on the **Plans** tab, select a paid plan, and open its **Pricing** section.
3. Click **Create missing in Stripe** to save a draft if needed, then create or reuse the plan's Stripe Product and recurring Prices. The returned `price_...` mappings are filled into that draft automatically.
4. Publish the updated draft. Repeat for each self-serve paid plan. Zero-cost and contact-sales plans are intentionally skipped.
5. Configure a Stripe Customer Portal configuration if you need a non-default portal.
6. Register `POST https://your-domain.example/stripe/webhook` for Checkout, subscription, and failed-payment events.
7. Set the webhook signing secret as `Stripe__WebhookSecret`.

For localhost, keep the Stripe CLI listener running while testing Checkout:

```bash
stripe listen \
  --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.paid,invoice.payment_failed \
  --forward-to https://localhost:5001/stripe/webhook \
  --skip-verify
```

Copy the listener's `whsec_...` value into `Stripe__WebhookSecret`, then restart the application. Checkout's success return also asks the server to verify the completed Checkout Session directly with Stripe. This closes the browser/webhook timing race and can recover a completed local checkout when the listener was not running. Webhooks remain required for renewals, payment failures, cancellations, and changes made later in the Customer Portal.

Catalog provisioning is metadata-based and idempotent, so retrying reuses template-managed Stripe objects instead of duplicating them. It is enabled for sandbox keys by default. Automatic live-mode provisioning requires the explicit deployment setting `Stripe__AllowLiveCatalogProvisioning=true`; otherwise live Products and Prices must be managed directly in Stripe and their IDs pasted into the plan draft.

## Individual and business accounts

Sign-up asks whether the customer is joining for themselves or for a business. Individual registration creates a private one-owner workspace; business registration requires an organization name and creates a team-ready workspace. Both start on Free without a payment method. The Personal plan is available only to Individual accounts; Pro, Business, and Enterprise are for Business organizations. `/admin/plans` controls plan audience on each draft version. Pricing filters the catalog by audience, and the API rechecks audience before Checkout. Individual accounts cannot invite members. A customer can explicitly create a separate business organization in Settings rather than silently changing an existing account's kind.

Deleting an eligible free individual account schedules its private workspace for the configured delayed deletion and removes the Identity account. Paid individual subscriptions must be canceled first; legal holds also block deletion. Business owners must transfer or delete their organizations before deleting their personal Identity account. A subscription change never converts an Individual account into a Business organization.

Until Price IDs and secrets exist, Free remains fully functional and paid checkout returns a descriptive configuration error. This makes the generated template useful immediately without accidentally creating live billing objects.

Administrators can enable a 1–365 day trial independently on each plan version from the **Plans** tab at `/admin/plans`. `Saas.EnableTrials`, `Saas.DefaultTrialDays`, and `Saas.TrialRequiresPaymentMethod` define the deployment-wide behavior; without an upfront payment method, Stripe cancels the subscription safely when its trial expires. The separate **Coupons** tab creates percentage or fixed-amount Stripe coupons, issues the accompanying customer-facing promotion code, sets duration, expiry, redemption limits, and first-purchase restrictions, and deactivates codes. Checkout displays Stripe's secure promotion-code entry automatically. Discount definitions and redemption state remain in Stripe; create/deactivate actions are recorded in the local SaaS audit trail.

Stripe-hosted invoice PDFs are the default. Add `ServiceStack.Pdf` only if the derived product needs a separate branded usage statement.

## Usage and quotas

See [Usage and quotas](https://react-templates.net/docs/next-saas/concepts/usage-and-quotas) for the full model and [Add a meter and quota](https://react-templates.net/docs/next-saas/development/add-a-meter-and-quota) to add your own.

Record consumption using a ServiceStack Identity session or scoped API key:

```bash
curl -X POST https://localhost:5001/api/RecordUsage \
  -H "X-Api-Key: $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "meterKey": "api.requests",
    "units": 12,
    "idempotencyKey": "import_01K4..."
  }'
```

The idempotency key is unique per workspace and meter operation. Replaying an accepted operation returns the existing result without spending quota twice. The included meters are `documents.stored`, `storage.bytes`, `documents.uploaded`, `api.requests`, and `workspace.seats`. Hard-limit operations that would exceed an allowance fail with `QuotaExceeded` and do not write a usage event.

The file upload example reserves document and byte capacity before streaming, settles the actual result, and releases capacity on failure. Background deletion writes compensating gauge adjustments.

Concurrent quota admission uses a conditional database update inside the same transaction as the immutable event or reservation. Two requests therefore cannot both spend the last unit. Upload failures also compensate any gauge settlement that completed before a later persistence failure.

## Developer workflow

Step-by-step recipes are in the [development guides](https://react-templates.net/docs/next-saas/development). For production, follow the [operations runbooks](https://react-templates.net/docs/next-saas/operations) and the [production security review](https://react-templates.net/docs/next-saas/security/production-security-review).

After changing C# request or response contracts, regenerate the browser client:

```bash
cd MyApp.Client
npm run dtos
```

Run the complete validation suite:

```bash
./scripts/verify.sh
```

Before deployment, export the production configuration and run the preflight. It validates configuration without printing secret values, then runs the complete build, test, static-export, and empty-database bootstrap suite:

```bash
set -a
source .env.production
set +a
./scripts/preflight.sh
```

Use `./scripts/preflight.sh --config-only` when build artifacts have already passed CI. An empty database bootstraps itself on first start; apply later migrations as a deliberate release step before starting multiple application instances:

```bash
cd MyApp
dotnet run --no-launch-profile --AppTasks=migrate
```

### Run the deployed database locally

[DATABASE.md](DATABASE.md) walks through choosing a provider, running it locally, and
configuring the same one for production. In short:

```bash
./scripts/dev-db.sh up        # start the provider in DB_PROVIDER and write the local config
./scripts/dev-db.sh status    # provider, container state, and connection target
./scripts/dev-db.sh shell     # psql, mysql, or sqlcmd against the local database
./scripts/dev-db.sh reset     # discard the local data volume and start empty
./scripts/dev-db.sh down      # stop the container, keeping its data
```

Locally and in production the database is `next_saas`, owned by an unprivileged `next_saas`
login; only the host and the password differ. PostgreSQL reuses `config/db/postgres/init.sh`
and SQL Server reuses `config/db/sqlserver/init.sql`, the same initializers the deployment
runs, so the two environments cannot drift. The local password is fixed and local-only
(`DEV_DB_PASSWORD`, default `Dev_Passw0rd!Local`); production passwords live only in
`DB_PASSWORD`. Point another tool at the same database with `eval "$(./scripts/dev-db.sh env)"`.

The connection is written into `.env`, which is gitignored and which the application applies in
Development, so any `Key__Sub` value there overrides the source-controlled settings on your
machine only. `MyApp/appsettings.Development.json` therefore stays on the SQLite default every
clone starts from, and a variable already set in your environment still wins over `.env`.

Production selects the same provider with a Kamal destination: `config/deploy.<provider>.yml` is
merged over `config/deploy.yml`, secrets come from `.kamal/secrets-common` plus
`.kamal/secrets.<provider>`, and `config/db/<provider>/pre-deploy.sh` provisions any accessory.
The Release workflow reads the `DB_PROVIDER` repository variable (default `sqlite`) and passes
`-d "$DB_PROVIDER"` to every `kamal` command, so switching providers changes one variable rather
than the pipeline:

```bash
export DB_PASSWORD="$(openssl rand -hex 32)"
./scripts/configure-deployment.sh \
  --service my-app \
  --repo owner/my-app \
  --set-github-secrets
```

`--provider` defaults to `$DB_PROVIDER`, so local and production stay on one switch.
`scripts/reset-kamal-deployment.sh` previews the exact service scope — containers, images,
accessories, app directories, and persistent state — before an explicit `--yes` reset. It clears
every provider's accessory, not just the selected one, so a provider switch leaves a clean slate.

See [Choose a database](https://react-templates.net/docs/next-saas/operations/choose-a-database)
and [config/README.md](config/README.md) for the full matrix and for adding another provider.

Or run individual commands:

```bash
dotnet build MyApp.slnx
dotnet test MyApp.slnx
cd MyApp.Client
npm run typecheck
npm run test:run
npm run build
```

Useful development surfaces:

- `/admin` — Operations Center overview, with focused routes under `/admin/*`;
- `/admin/customers` — Customer 360 and customer exceptions;
- `/admin/plans` — separate Plans and Coupons tabs;
- `/admin/usage` — platform analytics and quota pressure;
- `/admin/operations` — failed work queues and integration readiness;
- `/admin/security` — support access, retention, and platform audit;
- `/admin/settings` — configuration ownership and boundaries;
- `/admin-ui` — ServiceStack administration;
- `/admin-ui/database` — plan and customer record management;
- `/scalar/v1` — OpenAPI reference;
- `/ui` — ServiceStack API Explorer;
- `/up` — health check.
- `/ready` — database and file-store readiness.

To recreate the development database and local file storage from an empty state, whichever
provider is running:

```bash
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes
```

## Project structure

```text
MyApp/                       ASP.NET Core host, plugins, migrations, Stripe gateway
MyApp.Client/                Next.js static frontend and generated TypeScript DTOs
MyApp.ServiceInterface/      SaaS services, quota policy, Background Job commands
MyApp.ServiceModel/          shared domain entities and API contracts
MyApp.Tests/                 unit and integration tests
config/                      Kamal deployment configuration
scripts/                     database, verification, deployment, and reset tooling
```

The code favors explicit request DTOs, narrow services, configuration objects, and conventional folders. Those choices are intentionally predictable for both human maintainers and AI coding agents.
