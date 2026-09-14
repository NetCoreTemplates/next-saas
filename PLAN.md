# Next SaaS Template Plan

Status: implemented reference platform; external Stripe test-mode and optional PostgreSQL validation remain environment-specific  
Base template: `next-static`  
Target stack: .NET 10, ServiceStack, ASP.NET Core Identity, OrmLite, Next.js static export, React 19, TypeScript, Tailwind CSS, Stripe Billing

The template implements the billing foundation and the generic SaaS roadmap described here: public pricing, multi-workspace membership and roles, versioned plans, Stripe Checkout/Portal and webhook inbox, background event processing, atomic quota accounting, secure file storage, customer and operator analytics, ServiceStack API Keys, notifications, audit/export, lifecycle operations, SaaS administration, customer overrides, coupons, trials, health diagnostics, OpenAPI/Scalar, and production static export. Section 18 remains the detailed design and acceptance reference. Stripe test-mode scenarios and optional PostgreSQL coverage require external services and are intentionally not part of the zero-dependency SQLite verification command.

The visible reference product is **Acme**, a hosted document storage and analytics service. Acme intentionally implements only basic file upload, storage, deletion, and download as a reference vertical slice for document-count and storage-size quotas. Crawling, document parsing, semantic indexing, RAG, AI assistants, and advanced ingestion are not template goals. The reusable SaaS, billing, identity, entitlement, metering, analytics, and operations architecture remains product-neutral.

## 1. Product goal

`next-saas` is a production-oriented starting point for self-serve SaaS products. It provides the common platform capabilities that are expensive to retrofit correctly:

- public marketing and pricing pages;
- authentication and account management;
- workspaces, memberships, and roles;
- Stripe subscriptions and hosted billing management;
- versioned plans, features, and quotas;
- scoped API keys;
- exact per-period usage accounting and quota enforcement;
- customer billing and usage dashboards;
- operational administration, reconciliation, and audit history;
- background processing, health checks, request diagnostics, and deployment guidance.

The template is for subscription software and API products. It is not a physical-product commerce template and does not include carts, inventory, shipping, fulfillment, marketplace payouts, or Stripe Connect.

## 2. Architecture decision

Use `next-static` as the implementation base and import selected capabilities from `nextjs`.

The production application has one runtime: ASP.NET Core. Next.js creates static HTML, CSS, and JavaScript during the build; ASP.NET Core serves those files and owns every dynamic operation. This gives public pages good crawlability and metadata while avoiding a second production server and an additional location for business logic or secrets.

Next.js Route Handlers, Server Actions, and request-time React Server Components are intentionally out of scope. If a derived product later requires request-time rendering, it can migrate the presentation layer to the `next-rsc` hosting model without changing the ServiceStack contracts or billing domain.

Import these capabilities from `nextjs`:

- ServiceStack API Keys;
- RDBMS request logging and profiling;
- OpenAPI/Scalar support;
- SWR client helpers;
- MDX support for documentation and content;
- reusable accessible UI component primitives.

Do not import the Bookings, Todos, AI Chat, or other showcase domains.

## 3. Guiding boundaries

### Stripe owns

- Customers;
- Products and Prices used for payment;
- Checkout and payment-method collection;
- invoices, payments, credits, coupons, promotion-code redemptions, and tax calculation;
- subscription billing status and billing-period boundaries;
- the hosted Customer Portal.

### The application owns

- workspaces and membership;
- the mapping between an application plan version and Stripe Prices;
- feature authorization;
- numeric quotas and enforcement;
- the real-time immutable usage ledger;
- customer-specific entitlement or quota overrides;
- the local projection of Stripe state used on application requests;
- application access policy during trials, delinquency, cancellation, and grace periods.

Stripe must never be queried on the normal API authorization path. Webhooks and reconciliation jobs update the local projection.

Stripe meter events are an optional billing export. They are not the enforcement ledger because Stripe aggregates them asynchronously.

Request logs are diagnostic records. They are not the usage ledger because they are buffered, can be sampled or retained differently, and do not model idempotency or variable units.

Short-term rate limiting and commercial quotas are separate:

- rate limits protect service capacity over seconds or minutes;
- quotas enforce plan allowances over a billing period.

## 4. Configuration ownership and precedence

There are three deliberate configuration scopes.

### 4.1 Deployment-wide policy: JSON configuration

Global behavior belongs in the `Saas` and `Stripe` sections of `appsettings.json`, overridden by environment-specific JSON or deployment secrets.

Global configuration is visible read-only in Admin UI. It is not modified from the browser because changing deployed configuration should remain reviewable and reproducible.

Examples:

- default plan code;
- default currency and supported currencies;
- whether Free plans, trials, annual billing, seat billing, customer overrides, and metered overages are enabled;
- default trial and grace durations;
- plan-change policy;
- warning thresholds;
- API meter definitions and unit semantics;
- data-retention and reconciliation schedules;
- Stripe publishable key, secret key, webhook secret, and Portal configuration ID;
- public base URL and webhook URL;
- feature kill switches.

Secrets are supplied by environment variables or `APPSETTINGS_PATCH`; checked-in JSON contains empty placeholders only.

Proposed structure:

```json
{
  "Saas": {
    "DefaultPlan": "free",
    "DefaultCurrency": "usd",
    "SupportedCurrencies": ["usd"],
    "EnableFreePlan": true,
    "EnableTrials": true,
    "DefaultTrialDays": 14,
    "TrialRequiresPaymentMethod": false,
    "EnableAnnualBilling": true,
    "EnableSeatBilling": true,
    "EnableCustomerOverrides": true,
    "EnableMeteredOverages": false,
    "UpgradePolicy": "ImmediateWithProration",
    "DowngradePolicy": "AtPeriodEnd",
    "CancellationPolicy": "AtPeriodEnd",
    "PastDueGraceDays": 7,
    "AfterGracePolicy": "DowngradeToFree",
    "QuotaWarningPercentages": [80, 90, 100],
    "Meters": [
      { "Key": "documents.processed", "DisplayName": "Documents processed", "UnitName": "document", "DefaultEnforcement": "HardLimit" },
      { "Key": "storage.mb", "DisplayName": "Storage", "UnitName": "MB", "DefaultEnforcement": "HardLimit" },
      { "Key": "assistant.questions", "DisplayName": "AI questions", "UnitName": "question", "DefaultEnforcement": "HardLimit" },
      { "Key": "search.queries", "DisplayName": "Search queries", "UnitName": "query", "DefaultEnforcement": "HardLimit" }
    ]
  },
  "Stripe": {
    "PublishableKey": "",
    "SecretKey": "",
    "WebhookSecret": "",
    "PortalConfigurationId": "",
    "EnableCatalogProvisioning": true,
    "AllowLiveCatalogProvisioning": false
  }
}
```

### 4.2 Plan policy: RDBMS, managed by administrators

Plan definitions, versions, Stripe Price mappings, features, quota amounts, trial duration, display ordering, visibility, and availability are RDBMS records. Admin UI manages draft versions and publishes them.

Published plan versions are immutable. Editing a plan creates a draft version. New subscriptions use the newly published version; existing subscriptions remain pinned until an administrator schedules a migration. This prevents an innocent plan edit from silently changing a customer contract.

### 4.3 Customer-specific behavior: RDBMS, managed by administrators

Negotiated limits, complimentary access, grace extensions, custom pricing references, and support exceptions are customer overrides in the RDBMS. Every override records who changed it, why, its validity window, and its audit history.

Precedence for effective access is:

1. active customer override;
2. the workspace subscription's pinned plan version;
3. the current default Free plan version;
4. deployment-wide fallback policy.

## 5. Defaults chosen for the broadest SaaS audience

| Decision | Default | Configurable at |
| --- | --- | --- |
| Billing owner | Workspace | Architectural invariant |
| Individual customers | Automatic personal workspace | JSON enable/disable |
| Team model | Owner, Admin, Billing, Member | JSON role policy; membership in RDBMS |
| Public tiers | Free, Pro, Business; Enterprise is contact-sales | RDBMS Admin UI |
| Billing intervals | Monthly primary; annual supported | JSON capability; RDBMS Price mappings |
| Free access | Enabled with useful but bounded quotas | JSON capability; RDBMS limits |
| Trial | 14 days on seeded paid self-serve plans; independently switchable | JSON default; plan version in RDBMS Admin UI |
| Upgrade | Immediate with Stripe proration | JSON |
| Downgrade | Scheduled for period end | JSON |
| Cancellation | Access through paid period, then Free | JSON |
| Failed payment | Seven-day grace, then Free/read-only as needed | JSON default; customer override in RDBMS |
| Quota enforcement | Hard limit | Meter JSON default; plan quota override |
| Quota warning | 80%, 90%, and 100% | JSON |
| Overage billing | Supported but disabled | JSON capability; plan quota in RDBMS |
| Quota reset | Stripe subscription period | Architectural invariant |
| Free-plan reset | Calendar month in UTC | JSON |
| Carry-over | Disabled | Plan quota in RDBMS |
| API keys | Multiple workspace keys, scoped and revocable | Per workspace in RDBMS |
| Seat billing | Supported, disabled until Price mapping exists | JSON capability; plan version in RDBMS |
| Tax | Stripe Tax-compatible, disabled until configured | JSON and Stripe Dashboard |
| Coupons/promotion codes | Stripe-backed; accepted in Checkout and managed in SaaS Admin | Stripe with local audit history |
| Invoice PDFs | Use Stripe-hosted invoice PDFs | Architectural default |
| Custom usage statement PDF | Not generated by default | Optional ServiceStack.Pdf add-on |

Rationale:

- A workspace owner supports both individual and B2B products; a personal workspace keeps the individual flow simple.
- Three self-serve tiers give enough room for quota differentiation without producing an overwhelming pricing matrix.
- Monthly billing minimizes commitment; annual support covers the common discounted-commitment path without forcing it.
- A useful Free tier lets the generated project run and demonstrate quota behavior without requiring Stripe configuration.
- Hard limits avoid surprise invoices. Hybrid base-plus-overage billing remains available for API and infrastructure products.
- Immediate upgrades satisfy customers asking for more capacity. Delayed downgrades avoid complicated mid-period entitlement reduction.
- Cancellation and payment problems never delete customer data. They alter access policy only.

## 6. Domain model

All domain tables use audit fields. Public identifiers are GUIDs or opaque strings; sequential database IDs are not exposed where enumeration is undesirable.

### Workspace and identity

`Workspace`

- `Id`, `Name`, `Slug`, `Status`;
- `BillingEmail`;
- `StripeCustomerId`;
- `CreatedBy`, `CreatedDate`, `ModifiedBy`, `ModifiedDate`.

`WorkspaceMember`

- `WorkspaceId`, `UserId`;
- `Role` (`Owner`, `Admin`, `Billing`, `Member`);
- `Status`, invitation fields, audit fields;
- unique `(WorkspaceId, UserId)`.

`WorkspaceApiKey`

- maps a ServiceStack API Key to a workspace;
- optional acting user;
- name, scopes, expiry, last-used timestamp, revoked timestamp;
- the raw key is shown only at creation and is never stored in reversible application fields.

Interactive Identity sessions do not contain or impersonate an API key. APIs explicitly choose whether they accept Identity Auth, API Key Auth, or both.

### Plan catalog

`SaasPlan`

- stable code, display name, description, display order;
- public/hidden/contact-sales/archive flags.

`SaasPlanVersion`

- plan ID and monotonically increasing version;
- `Draft`, `Published`, `Retired` status;
- effective dates and publication audit fields;
- published versions are immutable.

`SaasPlanPrice`

- plan version, Stripe Product ID and Price ID;
- currency, monthly/annual interval, tax behavior;
- seat or flat-rate billing mode;
- active/archived state.

`SaasPlanFeature`

- plan version and stable feature key;
- enabled flag and optional settings JSON.

`SaasPlanQuota`

- plan version and meter key;
- included units;
- enforcement mode (`HardLimit`, `SoftLimit`, `MeteredOverage`, `Unlimited`);
- optional overage Price/meter mapping;
- carry-over policy and maximum carry-over.

### Billing projection

`BillingSubscription`

- workspace ID;
- Stripe Customer and Subscription IDs;
- pinned plan version and Stripe Price ID;
- status and cancellation flags;
- trial, current-period, grace, cancellation, and ended timestamps;
- Stripe object's latest known update/version details;
- reconciliation timestamp and error status.

`WorkspaceEntitlement`

- workspace and feature key;
- enabled/effective dates;
- source plan version or override;
- materialized for fast authorization.

`WorkspacePolicyOverride`

- workspace;
- optional feature key or meter key;
- override kind and value;
- reason, valid-from/until, created-by, approval/audit fields.

### Usage

`UsagePeriod`

- workspace, period start/end, subscription ID;
- status (`Open`, `Closing`, `Closed`);
- unique `(WorkspaceId, PeriodStart, PeriodEnd)`.

`UsageAggregate`

- workspace, period, meter key;
- consumed and reserved units;
- row version/concurrency token;
- unique `(WorkspaceId, UsagePeriodId, MeterKey)`.

`UsageEvent`

- immutable event ID and unique idempotency key;
- workspace, period, meter key, signed quantity;
- user ID, API key ID, request ID;
- operation/request DTO name;
- occurred/recorded timestamps;
- optional dimensions and safe metadata JSON;
- source event for corrections.

`UsageReservation`

- reservation ID and idempotency key;
- workspace, period, meter, reserved units;
- `Pending`, `Settled`, `Released`, `Expired` status;
- actual settled units and expiry.

### Stripe reliability

`StripeEventInbox`

- unique Stripe event ID;
- event type, Stripe creation timestamp, livemode flag;
- payload or protected payload reference;
- `Pending`, `Processing`, `Processed`, `Failed`, `Ignored` status;
- attempt count, last error, next attempt, processed timestamp.

`StripeUsageOutbox`

- usage event/aggregate reference;
- Stripe customer, meter name, quantity, timestamp;
- idempotency key;
- delivery status, attempts, and last error.

`BillingAuditEvent`

- immutable operator/system activity log for plan publication, subscription reconciliation, overrides, event replay, and corrections.

## 7. Subscription access policy

The local subscription projection drives access.

| Local state | Effective access |
| --- | --- |
| No paid subscription | Current Free plan |
| Trialing | Paid plan version until trial end |
| Active | Paid plan version |
| Cancel at period end | Paid plan through current period end |
| Past due within grace | Paid plan with warning banner |
| Past due after grace | Free plan; operations above Free limits are read-only or blocked |
| Paused, unpaid, or ended | Free plan unless an active override applies |

Data is not deleted automatically when access is reduced. Destructive retention behavior is a separate global policy and should be opt-in.

The Checkout return page shows `Activating your subscription…` while the authenticated API verifies the returned Checkout Session directly with Stripe and refreshes the local projection. The server validates the session's workspace metadata and client reference before applying it, so access is never granted based solely on browser-controlled query data. Signed webhooks remain authoritative for renewals, payment failures, cancellations, Portal changes, and reconciliation.

## 8. Stripe integration

Use the official `Stripe.net` package through an injected `StripeClient`; do not use global mutable Stripe configuration.

### Checkout

1. Require an authenticated workspace Owner or Billing member.
2. Accept an internal plan code/version and billing interval.
3. Resolve an allowlisted active `SaasPlanPrice`; never trust a browser-supplied Price ID.
4. create or reuse the workspace's Stripe Customer.
5. include workspace and plan identifiers in Stripe metadata and `client_reference_id`.
6. use an application idempotency key.
7. return the hosted Checkout URL.

### Customer Portal

- Require Owner or Billing role.
- Create a short-lived Portal session for the workspace's Stripe Customer.
- Allow payment-method updates, invoice access, cancellation, and supported plan changes.
- Continue to treat webhooks as authoritative after the customer leaves the Portal.

### Webhooks

1. Read the untouched raw UTF-8 request body.
2. verify `Stripe-Signature` using the configured endpoint secret.
3. insert `StripeEventInbox` under a unique event-ID constraint.
4. return `2xx` after durable acceptance, including for an already accepted duplicate.
5. process the inbox event with a ServiceStack Background Job.
6. do not depend on delivery order; retrieve the current Stripe Customer, Subscription, Invoice, or Entitlement when needed.
7. update the local projection transactionally and append a billing audit event.
8. retry transient failures with bounded backoff; expose permanent failures in Admin UI.

Initial event coverage:

- `checkout.session.completed`;
- `customer.subscription.created`;
- `customer.subscription.updated`;
- `customer.subscription.deleted`;
- `invoice.paid`;
- `invoice.payment_failed`;
- `invoice.finalized` where invoice metadata is displayed;
- `entitlements.active_entitlement_summary.updated` when optional Stripe Entitlements sync is enabled;
- Stripe meter error events when overages are enabled.

### Reconciliation

A recurring ServiceStack Job reconciles active local subscriptions against Stripe. It repairs missed/out-of-order webhook effects and records discrepancies. Administrators can reconcile one workspace or replay one accepted event from Admin UI.

### Invoices and PDFs

Display Stripe's hosted invoice and invoice PDF links. Do not regenerate a legal invoice with ServiceStack.Pdf.

ServiceStack.Pdf is appropriate only for an optional branded usage statement that is clearly labeled as a usage report rather than a tax invoice.

## 9. Usage and quota engine

### Meter definition

Meter keys and unit semantics are deployment-wide JSON because application code consumes them. Plan versions assign allowances and enforcement modes in the RDBMS.

Acme examples:

- `documents.processed` — one unit per successfully processed document;
- `storage.mb` — megabytes retained by the workspace;
- `assistant.questions` — one unit per accepted grounded-answer request;
- `search.queries` — one unit per document search;
- `workspace.seats` — current active membership projection.

### Declarative fixed usage

Prefer an explicit ServiceStack request attribute or request filter:

```csharp
[ValidateApiKey]
[RequiresEntitlement("api.access")]
[ConsumesQuota("documents.processed", Units = 1)]
public class ExampleApi : IPost, IReturn<ExampleApiResponse>
{
}
```

### Variable usage

Services inject `IUsageMeter` and use a reservation:

```csharp
await using var usage = await usageMeter.ReserveAsync(
    request: Request,
    meter: "pages.processed",
    maximumUnits: request.PageCount,
    cancellationToken);

var processed = await processor.ProcessAsync(request, cancellationToken);
await usage.SettleAsync(processed.PageCount, cancellationToken);
```

### Atomic enforcement

The reserve operation performs a conditional database update inside a transaction:

`ConsumedUnits + ReservedUnits + requested <= effective limit`

Only the request that successfully changes the aggregate may continue. The transaction also inserts the idempotent reservation/event record. This prevents concurrent requests from both consuming the final units.

Do not reset aggregate rows in place. Select or create the aggregate for the current `UsagePeriod`, which makes boundary rollover deterministic and auditable.

Return standard rate-limit headers where suitable plus explicit SaaS headers:

- `X-Quota-Meter`;
- `X-Quota-Limit`;
- `X-Quota-Used`;
- `X-Quota-Remaining`;
- `X-Quota-Reset`.

Use stable error codes such as `QuotaExceeded`, `FeatureNotEntitled`, `SubscriptionPastDue`, and `RateLimitExceeded` so clients can distinguish commercial and capacity failures.

## 10. API surface

### Public and authenticated customer APIs

- `GetSaasPlans`;
- `GetWorkspace` and `UpdateWorkspace`;
- `QueryWorkspaceMembers`, invite/update/remove member commands;
- `GetBillingSummary`;
- `CreateCheckoutSession`;
- `CreateBillingPortalSession`;
- `GetUsageSummary`;
- `QueryUsageEvents`;
- `QueryWorkspaceApiKeys`, create/update/revoke key commands.

### Webhook API

- `POST /webhooks/stripe` with raw-body signature verification;
- excluded from ordinary API metadata and request-body logging;
- no Identity or API-key authentication because Stripe signature validation is its authentication mechanism.

### Administrative APIs

- query plans and versions;
- create/edit a draft and publish/retire a version;
- validate or synchronize Stripe Price mappings;
- query workspaces and subscriptions;
- create/expire customer overrides;
- query usage, reservations, corrections, inbox/outbox, and audit events;
- reconcile a subscription;
- retry/replay an inbox event;
- issue a compensating usage correction;
- preview effective entitlements and quotas for a workspace.

Administrative state-changing APIs use explicit custom ServiceStack services with validation and audit behavior. AutoQuery is used for rich authorized queries and safe CRUD records, not for transitions that have Stripe side effects or immutable-state rules.

## 11. User interface

### Public Next.js pages

- `/` — SaaS landing page;
- `/pricing` — plan/version-driven comparison and monthly/annual selector;
- `/features`;
- `/docs` and optional MDX content;
- `/privacy` and `/terms`;
- `/signin` and `/signup`.

The pricing page renders useful build-time fallback content for SEO, then refreshes published plans from the .NET API in the browser. It displays billing intervals and prices only when a valid active Stripe Price mapping exists.

### Authenticated customer pages

- `/dashboard` — onboarding and current plan summary;
- `/usage` — progress bars, meter history, reset dates, and warnings;
- `/billing` — subscription status, plan change, invoices, and Portal link;
- `/api-keys` — reveal-once creation, scopes, expiry, rotation, and revocation;
- `/team` — members, invitations, and roles;
- `/settings` — workspace and personal settings.

### ServiceStack Admin UI

Use ServiceStack's existing capability-based `/admin-ui` and `/auto` experiences instead of building a second admin framework.

Enable and retain:

- Admin Users;
- Admin Database;
- RDBMS Request Logs and profiling;
- Background Jobs;
- validation management where useful;
- AutoQuery schema/admin UI for authorized SaaS models.

Add a small SaaS-specific Admin UI module under `wwwroot/modules/admin-ui` with:

1. **Overview** — active/trialing/past-due counts, failed jobs, webhook backlog, usage export backlog.
2. **Plans** — draft/version lifecycle, feature matrix, quota matrix, prices, publication and migration actions.
3. **Customers** — workspace search, subscription state, effective access, overrides, reconcile action.
4. **Usage** — top consumers, per-meter totals, rejected requests, reservations, corrections.
5. **Stripe Events** — inbox status, error details, safe replay/retry, related workspace.
6. **System Policy** — read-only effective JSON configuration with secret values redacted.

Use AutoQuery-generated `/auto` grids for ordinary authorized data browsing. Use custom Admin APIs and forms for plan publication, subscription changes, event replay, and usage corrections.

Admin roles:

- `Admin` — full access;
- `BillingAdmin` — plans, subscriptions, Stripe events, and overrides;
- `Support` — read access plus reconcile; no plan publication or usage correction.

## 12. ServiceStack features to use

- `IdentityAuth` and Admin Users for authentication and user administration;
- `ApiKeysFeature` for scoped API credentials;
- AutoQuery for administrative queries and safe CRUD;
- declarative validation and authorization attributes;
- `BackgroundsJobFeature` and Commands for webhook processing, reconciliation, notifications, usage export, and cleanup;
- RDBMS Request Logs and profiling for diagnostics;
- Admin Database for development and emergency inspection;
- OpenAPI/Scalar for API consumers;
- health checks for database, job backlog, and Stripe configuration readiness;
- generated TypeScript DTOs as the only browser API contracts;
- `ServiceStack.Pdf` only for an optional non-invoice usage statement.

Avoid introducing a second job runner, API framework, authentication system, generic repository layer, or Node-side backend.

## 13. Background jobs

Commands/jobs:

- `ProcessStripeEventCommand` — one durable inbox event;
- `ReconcileStripeSubscriptionCommand` — one workspace/subscription;
- `ReconcileAllStripeSubscriptionsCommand` — recurring dispatcher;
- `ExportStripeUsageCommand` — batched outbox delivery when overages are enabled;
- `SendQuotaWarningCommand`;
- `SendPaymentFailedCommand`;
- `ExpireUsageReservationsCommand`;
- `CloseUsagePeriodCommand` where finalization is needed;
- `MigratePlanSubscribersCommand` for an explicitly approved version migration.

Jobs must be idempotent and safe to retry. A job stores only stable IDs; it reloads current records when executing.

## 14. Security and privacy requirements

- Stripe secrets are never included in Next.js environment variables or browser bundles.
- Verify webhook signatures against the raw body before parsing.
- Exclude Stripe webhooks, authorization headers, API keys, passwords, tokens, and sensitive DTO fields from request-body logs.
- Disable broad request-body logging by default; use explicit allowlists or redaction.
- API key material is reveal-once and stored using ServiceStack's safe representation.
- Checkout and Portal APIs require workspace billing authorization.
- Administrators cannot supply arbitrary redirect hosts; URLs derive from configured `PublicBaseUrl`.
- Every external Stripe mutation uses idempotency keys.
- Every admin mutation is authorized, validated, and audited.
- Usage metadata has a documented allowlist and must not contain request bodies or customer content.
- Customer deletion and privacy export are explicit audited workflows, not side effects of subscription cancellation.
- Apply separate partitioned rate limits to anonymous, Identity, and API-key traffic.

## 15. AI-agent-friendly repository conventions

- Keep one feature per clearly named contract/service/domain file.
- Prefer direct domain names over framework abstractions: `StripeBillingService`, `EntitlementService`, `UsageMeter`, `PlanCatalog`.
- Keep C# request/response DTOs authoritative and regenerate `lib/dtos.ts`.
- Add `ARCHITECTURE.md`, `BILLING.md`, and `QUOTAS.md` before implementation completes.
- Extend `AGENTS.md` with state-machine invariants, configuration ownership, commands, and testing recipes.
- Use built-in `TimeProvider` for deterministic billing-period and grace-policy tests.
- Wrap `StripeClient` behind a narrow interface only where needed for tests; do not recreate the Stripe SDK models.
- Include a complete sample vertical slice with one fixed-unit API and one reservation/settlement API.
- Include Stripe fixture payloads with fake identifiers and no secrets.
- Keep default plans and seed data deterministic.
- Add comments for business invariants, not obvious syntax.

## 16. Testing strategy

### Unit tests

- entitlement resolution and override precedence;
- subscription state-to-access policy;
- plan version immutability and publication;
- billing-period resolution;
- hard, soft, unlimited, and overage quota decisions;
- reservation settlement, release, and expiry;
- proration/downgrade/cancellation command options;
- safe log redaction.

### Database/integration tests

- two concurrent requests competing for the final quota unit;
- duplicate usage idempotency keys;
- period rollover at exact UTC boundaries;
- duplicate Stripe webhook delivery;
- out-of-order subscription and invoice events;
- failed job retry and dead-letter visibility;
- customer override activation and expiry;
- plan migration batches;
- API key workspace attribution.

Run concurrency tests against PostgreSQL as well as SQLite because production locking behavior matters.

### UI tests

- pricing interval and plan selection;
- Checkout and Portal redirects using mocked APIs;
- provisioning state after Checkout return;
- quota warnings and exhausted state;
- permission-aware team and billing controls;
- Admin plan draft/publish flow;
- Admin webhook retry and customer reconciliation.

### Stripe test-mode acceptance

- Stripe CLI forwards signed fixture events;
- a Checkout subscription provisions only after webhook processing;
- upgrade, downgrade, cancellation, renewal, and payment failure converge correctly;
- a replayed event produces no duplicate side effects;
- optional meter export uses stable idempotency identifiers.

## 17. Implementation sequence

### Milestone 0 — Fork hygiene

- rename template metadata, package names, URLs, screenshots, and deployment identifiers;
- remove inherited demo content and `Configure.secrets.cs` examples;
- update dependencies and lock files;
- require clean build, test, and dependency audit;
- update README and AGENTS guidance.

Exit criterion: the renamed empty SaaS shell builds and its smoke test passes with no Stripe configuration.

### Milestone 1 — Workspace foundation

- workspace/member migrations and services;
- automatic personal workspace on registration;
- active-workspace resolution;
- membership authorization;
- customer dashboard shell and team management;
- Admin/AutoQuery workspace views.

Exit criterion: individual and team access are isolated and covered by integration tests.

### Milestone 2 — Plan catalog and Admin UI

- plan/version/price/feature/quota schema;
- deterministic Free, Pro, and Business seed data;
- draft, validation, publication, retirement, and migration services;
- plan and quota Admin UI;
- public pricing API and page.

Exit criterion: an administrator can safely publish a new plan version without altering existing subscriptions.

### Milestone 3 — Stripe billing

- Stripe.net client and typed configuration;
- Checkout and Portal services;
- webhook inbox and processor;
- subscription projection and access policy;
- recurring reconciliation;
- billing customer UI and operational Admin UI.

Exit criterion: all subscription lifecycle cases converge under duplicate and out-of-order events.

### Milestone 4 — API keys and quota engine

- workspace-scoped ServiceStack API Keys;
- meter catalog and entitlement attributes;
- atomic aggregates, immutable events, and reservations;
- fixed and variable sample APIs;
- customer usage dashboard and Admin usage UI;
- rate-limit policies and quota response headers.

Exit criterion: concurrent quota enforcement cannot overspend a hard limit.

### Milestone 5 — Operations and release

- notifications and warning jobs;
- optional Stripe meter outbox;
- request logging/profiling with redaction;
- health/readiness checks;
- PostgreSQL deployment profile;
- Stripe CLI setup, fixtures, documentation, and end-to-end tests;
- example customer overrides and support workflows.

Exit criterion: a newly generated application can complete setup, Checkout, usage enforcement, Portal management, webhook replay, and reconciliation from documented commands.

## 18. Post-foundation generic SaaS roadmap

### 18.1 Scope and delivery principles

The objective of the remaining roadmap is to maximize the value of `next-saas` as a starting point for the largest practical set of self-serve and B2B subscription products. Acme provides understandable sample nouns, but new platform capability must remain usable when an adopter replaces documents with projects, messages, reports, devices, seats, API calls, or another billable resource.

The roadmap deliberately does not turn Acme into a document-processing product. The sample file feature exists to demonstrate five reusable platform concerns in one small vertical slice:

1. tenant-owned data;
2. counter and gauge quotas;
3. reservation, commit, and release around fallible work;
4. customer and operator analytics;
5. secure storage and deletion lifecycle.

Implementation rules for every phase:

- enforce access, features, and quotas on the server; the browser only reflects decisions;
- establish a server-verified workspace context before accessing tenant-owned resources;
- keep C# DTOs authoritative and regenerate `MyApp.Client/lib/dtos.ts`;
- keep deployment policy in JSON, plan and customer policy in the RDBMS, and secrets in deployment secrets;
- use ServiceStack Background Jobs for durable asynchronous work and recurring schedules;
- use AutoQuery for safe administrative reads, but custom Services for state transitions;
- make every command with external or retryable effects idempotent;
- retain a useful no-Stripe, SQLite, local-filesystem development experience;
- add PostgreSQL integration coverage for concurrency and isolation behavior;
- update the operator documentation and sample configuration with each phase.

### 18.2 Baseline and priority

The following foundation already exists and is not reimplemented by this roadmap: authentication, workspaces and basic roles, plans and immutable plan versions, plan quotas and features as data, Stripe Checkout and Customer Portal, webhook inbox processing, coupons, trials, usage events and aggregates, hard quota enforcement, customer overrides, API keys, basic customer pages, and SaaS operator administration.

| Priority | Workstream | Primary value | Depends on |
| --- | --- | --- | --- |
| 1 | Effective feature entitlements | Makes plan differentiation reusable in product code | Existing plan catalog |
| 2 | Tenant isolation hardening | Makes every derived SaaS safe by default | Existing workspace model |
| 3 | Generalized quota and metering engine | Supports the common counter, gauge, and reservable billing models | Entitlements, tenant context |
| 4 | Basic file-storage example | Proves quota and storage patterns end to end | Metering, tenant isolation |
| 5 | Customer usage analytics | Makes limits understandable and actionable | Metering, file sample |
| 6 | Platform SaaS analytics | Helps operators run and improve the business | Customer analytics, billing projection |
| 7 | Subscription lifecycle policy | Makes trials, payment problems, cancellation, and recovery predictable | Entitlements, billing projection |
| 8 | Transactional notifications | Communicates lifecycle and quota events reliably | Background Jobs, lifecycle events |
| 9 | General audit trail | Provides accountability, security evidence, and support context | Tenant context, event conventions |
| 10 | Support and operations tools | Reduces day-to-day operational cost | Audit trail, lifecycle services |
| 11 | Data lifecycle and self-service | Supplies safe export, deletion, and ownership workflows | Jobs, audit, storage abstraction |
| 12 | Template configuration and agent ergonomics | Makes the project fast to adopt and rebrand | Stable platform surface |

Phases should be delivered in this order. A phase is complete only when its APIs, UI, authorization, tests, documentation, and observability are complete.

### 18.3 Phase 6 — Effective feature entitlements

#### Outcome

An adopter can define a stable feature key once, include it in plan versions, override it for a customer, enforce it in a ServiceStack API, and render an appropriate enabled, disabled, or upgrade state in React.

#### Configuration and data

- Add a deployment-wide feature registry under `Saas.Features` in `appsettings.json` containing `Key`, `DisplayName`, `Description`, `Category`, and `DefaultEnabled`.
- The registry defines which keys application code understands; it does not grant a customer access.
- Continue storing plan grants in `SaasPlanFeature` and customer exceptions in `CustomerEntitlementOverride`.
- Support boolean grants first. Retain `SettingsJson` for typed product-specific settings without creating a generic expression language.
- Add an optional `ExpiresAt` to temporary customer feature overrides and ensure expired overrides are ignored without destructive cleanup.
- Do not require a materialized `WorkspaceEntitlement` table initially. Resolve from the pinned plan version and active overrides; add a projection only after profiling proves it necessary.

Example registry:

```json
{
  "Saas": {
    "Features": [
      { "Key": "analytics.advanced", "DisplayName": "Advanced analytics", "Category": "Analytics", "DefaultEnabled": false },
      { "Key": "branding.custom", "DisplayName": "Custom branding", "Category": "Customization", "DefaultEnabled": false },
      { "Key": "api.access", "DisplayName": "API access", "Category": "Developer", "DefaultEnabled": false },
      { "Key": "audit.read", "DisplayName": "Audit logs", "Category": "Security", "DefaultEnabled": false }
    ]
  }
}
```

#### Backend

- Add `IEntitlementResolver.GetEffectiveEntitlementsAsync(workspaceId)` and `HasFeatureAsync(workspaceId, featureKey)`.
- Return the effective value, source (`Override`, `Plan`, `FreeFallback`, `GlobalFallback`), plan version, and override expiry for diagnostic use.
- Add a `RequiresFeatureAttribute`/request filter for declarative API enforcement.
- Return HTTP `403` with stable code `FeatureNotEntitled`, feature key, and optional recommended plan code.
- Cache effective entitlements by workspace and policy version; include workspace ID in the key and invalidate after plan publication, subscription projection changes, or overrides.
- Reject unknown feature keys when editing a plan or override.

#### APIs and UI

- Add `GetEffectiveEntitlements` for the active workspace.
- Add entitlement summaries to `GetSaasDashboard` and `GetSaasAdmin`.
- Add `useEntitlements()`, `hasFeature(key)`, and an accessible `FeatureGate` React component.
- A disabled control must explain why it is unavailable and link to billing when an upgrade is possible.
- Extend the plan editor with a feature matrix built from the configured registry.
- Extend customer administration with effective-value/source diagnostics and temporary override expiry.

#### Tests and acceptance

- Test override → pinned plan → Free plan → global fallback precedence.
- Test expired overrides, unknown keys, cache invalidation, and subscription changes.
- Prove direct API calls cannot bypass a hidden or disabled client control.
- Acceptance: adding a feature to JSON plus a plan draft is sufficient to enforce and display that feature without adding billing-specific code.

### 18.4 Phase 7 — Tenant isolation hardening

#### Outcome

Tenant isolation becomes a cross-cutting, tested invariant rather than a convention each new service must remember.

#### Backend architecture

- Add `IWorkspaceContext` containing the authenticated user/API key, verified workspace membership, workspace role, and correlation ID.
- Resolve it once per tenant-scoped request from authenticated identity and current membership. A client-supplied workspace ID is a selector, never proof of authorization.
- Add reusable guards for `Member`, `Billing`, `Admin`, and `Owner` workspace capabilities.
- Require tenant-owned tables to implement a small `IHasWorkspaceId` marker or follow a documented schema convention.
- Provide OrmLite helpers that always include `WorkspaceId` in resource lookups and mutations.
- Require explicit, separately authorized platform-admin methods for cross-workspace queries.
- Namespace cache entries, idempotency keys, files, Background Jobs, usage records, and audit entries by verified workspace ID.
- Background Job payloads store stable workspace and resource IDs. Workers reload and re-authorize the target state instead of trusting serialized user input.

#### Storage and API-key rules

- File keys use an opaque workspace namespace and opaque file ID; never use a user-supplied path.
- Authorize the exact file and operation before issuing or serving a download.
- API keys are bound to one workspace, explicit scopes, expiry, and optional acting user.
- Rate limits include workspace and credential identity to prevent one tenant becoming a noisy neighbor.

#### Tests and acceptance

- Create a shared authorization matrix fixture for every workspace role and resource operation.
- Add cross-tenant read, update, delete, download, analytics, cache-reuse, API-key, and Background Job tests.
- Add a schema coverage test that classifies each product table as tenant-owned, global, or platform-operational.
- Run isolation tests through the same database user and pooling mode used in production.
- Acceptance: adding a tenant-owned table without classification or without tested cross-tenant denial fails CI.

### 18.5 Phase 8 — Generalized quota and metering engine

#### Outcome

The template cleanly supports the three quota shapes most SaaS products need: period counters, current-value gauges, and reservations for operations whose final cost is not known at admission time.

#### Meter model

Extend `Saas.Meters` definitions with:

- `Kind`: `Counter`, `Gauge`, or `ReservableCounter`;
- `Reset`: `BillingPeriod`, `CalendarMonth`, or `Never`;
- `Aggregation`: `Sum`, `Current`, or `Maximum`;
- `Precision` and canonical base unit;
- `AllowCustomerBreakdown` and `AllowUserBreakdown`;
- warning thresholds and default enforcement;
- optional Stripe meter/Price mapping used only for export.

Store canonical integer units. File storage uses bytes, not rounded megabytes. The UI formats values into KB/MB/GB.

Recommended Acme meters:

| Meter | Kind | Reset | Meaning |
| --- | --- | --- | --- |
| `documents.stored` | Gauge | Never | Current retained file count |
| `storage.bytes` | Gauge | Never | Current retained file bytes |
| `documents.uploaded` | Counter | Billing period | Successful uploads during the period |
| `api.requests` | Counter | Billing period | Example API consumption |

`documents.processed` and `storage.mb` should be replaced in the seed catalog because they do not accurately represent retained-document and exact-storage limits.

#### Data and services

- Add `UsageReservation` with workspace, meter, period, requested units, settled units, status, expiry, idempotency key, and audit timestamps.
- Extend `UsageAggregate` so counter totals and gauge current/high-water values are unambiguous.
- Keep accepted consumption in immutable `UsageEvent` rows.
- Represent gauge decrements and corrections as privileged, linked adjustment events; the public record-usage API continues accepting positive consumption only.
- Add `IUsageMeter.CheckAsync`, `ConsumeAsync`, `ReserveAsync`, `SettleAsync`, `ReleaseAsync`, and `AdjustGaugeAsync`.
- Make reservation admission atomic with aggregate update and unique idempotency insert in one transaction.
- Expire abandoned reservations with a recurring ServiceStack Job.
- Include effective limit, used, reserved, remaining, period/reset, warning state, and enforcement source in responses.
- Emit domain events when configured thresholds are crossed, exactly once per workspace/meter/period/threshold.

#### API and developer experience

- Keep declarative `[ConsumesQuota]` for fixed-cost requests.
- Add `[RequiresFeature]` and quota attributes to the same request-pipeline stage so errors are deterministic.
- Provide one documented fixed-cost example and the file upload reservation example.
- Return `X-Quota-*` headers and stable errors without exposing internal billing identifiers.
- Add an operator-only compensating adjustment API requiring a reason; never mutate historical events.

#### Tests and acceptance

- Cover exact-limit acceptance, over-limit rejection, idempotent retry, counter rollover, non-resetting gauges, reservation settlement/release/expiry, deletion decrement, and override precedence.
- Run concurrent final-unit and concurrent file-size reservations against SQLite and PostgreSQL.
- Verify threshold notifications are not duplicated under retries.
- Acceptance: two concurrent requests cannot both spend the last allowance, and a failed reserved operation restores all capacity.

### 18.6 Phase 9 — Basic file-storage reference feature

#### Outcome

Acme demonstrates secure file ownership, upload reservations, document-count limits, storage-size limits, analytics, and deletion without document parsing or ingestion logic.

#### Data model

Add `StoredFile`:

- opaque `Id` and required `WorkspaceId`;
- original display name and storage object key;
- content type, byte length, checksum, status, and optional description;
- uploaded-by, created, modified, and deleted audit fields;
- status: `Pending`, `Available`, `Deleting`, `Deleted`, or `Failed`;
- unique storage object key and indexes on `(WorkspaceId, CreatedDate)` and `(WorkspaceId, Status)`.

Do not store file bodies in the RDBMS.

#### Storage abstraction

- Define `IFileStore` with put, open-read, exists, and delete operations using opaque object keys.
- Ship `LocalFileStore` for development under an application-data directory excluded from source control.
- Document how a derived app can add S3, R2, or Azure Blob implementations without changing Services.
- Support configured maximum single-file size and an allow/deny MIME/extension policy in global JSON.
- Stream uploads and downloads; do not buffer entire files in memory.
- Compute a checksum while streaming and clean up partial objects after failure.

#### Upload transaction

1. authenticate and establish workspace context;
2. validate declared filename, content type, and length;
3. reserve one `documents.stored` unit and the declared `storage.bytes` units using one operation ID;
4. insert a `Pending` `StoredFile` row;
5. stream to a temporary object key and calculate actual bytes/checksum;
6. atomically mark the file `Available` and settle both reservations with actual values;
7. release reservations and delete partial content on failure;
8. make a retry with the same idempotency key return the same available file.

If the transport cannot reliably provide length, reserve the configured maximum or use a bounded streaming admission policy. Never accept an unbounded stream and check quota afterward.

Deletion marks the row `Deleting`, enqueues `DeleteStoredFileCommand`, removes the object idempotently, records negative gauge adjustments, and marks the row `Deleted`. Failed deletion remains visible to operators and is safe to retry.

#### APIs and UI

- `QueryStoredFiles` with workspace-scoped paging and safe filters;
- `UploadStoredFile` as a multipart ServiceStack request;
- `DownloadStoredFile` with exact-resource authorization;
- `DeleteStoredFile` as an idempotent command;
- `/documents` customer page with drag/drop, upload progress, storage summary, file table, download, and delete;
- empty, uploading, quota-exceeded, failed, and retry states;
- no document preview, parsing, folders, crawling, semantic search, or RAG.

#### Tests and acceptance

- Cover invalid type, oversized file, insufficient document quota, insufficient byte quota, interrupted stream, idempotent retry, cross-workspace download, and deletion recovery.
- Verify DB/file consistency after injected failures at every upload step.
- Acceptance: a user can upload, list, download, and delete files; file count and exact byte usage remain correct after retries and failures.

### 18.7 Phase 10 — Customer usage analytics

#### Outcome

Workspace administrators can understand current limits, historical consumption, likely exhaustion, and which users or API keys generate usage.

#### Data and aggregation

- Add `UsageDailyRollup` keyed by workspace, UTC date, meter, and optional safe dimension.
- Roll up immutable events with `BuildUsageRollupsCommand`; keep raw events authoritative.
- Permit near-real-time current-period cards directly from `UsageAggregate` and use rollups for charts.
- Maintain a rollup watermark so a retry recomputes an affected day idempotently.
- Only expose user/API-key breakdown for meters whose global definition permits it.
- Implement a simple period-end projection using current rate and elapsed period; label it as projected, not guaranteed.

#### APIs and UI

- `GetUsageOverview` for cards, warnings, reset dates, and upgrade targets;
- `QueryUsageSeries` for date/meter series with a bounded range;
- `QueryUsageBreakdown` for user, API key, or configured safe dimensions;
- `ExportUsageCsv`, produced synchronously for small ranges and by Background Job for large ranges;
- `/usage` gains meter selection, daily chart, limit markers, projected usage, top consumers, rejected-operation history, and CSV export;
- dashboard cards link to the filtered analytics view;
- quota warnings link to the relevant meter and billing upgrade action.

#### Tests and acceptance

- Test timezone/UTC boundaries, late events, correction events, rollup retry, authorization, dimension redaction, and projection with partial periods.
- Test charts with no data, unlimited plans, Free calendar months, paid billing periods, and expired customer overrides.
- Acceptance: displayed current totals reconcile exactly to aggregates and exported totals reconcile to immutable events.

### 18.8 Phase 11 — Platform SaaS analytics

#### Outcome

Platform operators receive actionable product, billing, usage, and operational health metrics without requiring an external BI tool for routine administration.

#### Metrics

Provide clearly defined metrics for:

- new and active workspaces;
- active, trialing, past-due, paused, canceling, and canceled subscriptions;
- trials starting, ending, converting, and expiring;
- plan and billing-interval distribution;
- coupon and promotion-code adoption;
- estimated MRR from the local Stripe projection, labeled as an estimate;
- usage by meter and plan;
- workspaces above warning thresholds or repeatedly rejected by quotas;
- file count and storage growth for the Acme example;
- webhook backlog/failures, job backlog/failures, and reconciliation drift.

Do not call Stripe while rendering the dashboard. Stripe-financial truth remains in Stripe; reconciliation refreshes the local projection.

#### Data, APIs, and UI

- Add `SaasDailySnapshot` for expensive historical platform metrics and recompute a bounded recent window to absorb late webhooks.
- Use live indexed queries for operational queues and current exception lists.
- Add `BuildSaasDailySnapshotCommand` and an operator-triggered rebuild command.
- Add `GetSaasAnalytics`, `QuerySaasMetricSeries`, and drill-down AutoQuery endpoints protected by platform roles.
- Expand the `/admin` overview and `/admin/usage` route with date range, summary cards, plan mix, trial funnel, usage concentration, quota pressure, revenue estimate, and operations health.
- Every card links to a filtered customer, subscription, usage, webhook, or job list.
- Apply minimum-group-size suppression or omit sensitive breakdowns that could expose individual customer behavior.

#### Tests and acceptance

- Define each metric in tests using a fixed `TimeProvider` and deterministic fixture workspaces.
- Cover trials crossing a date boundary, annual-price MRR normalization, coupons, cancellations, missing price mappings, late webhook updates, and snapshot rebuild idempotency.
- Acceptance: an operator can trace every aggregate card to its underlying records and can distinguish estimates from Stripe-authoritative values.

### 18.9 Phase 12 — Subscription lifecycle policy

#### Outcome

Trial, upgrade, downgrade, cancellation, delinquency, pause, and recovery behavior is explicit, configurable, and consistently reflected in authorization and UI.

#### State and policy

- Keep raw Stripe status separately from application access state.
- Add `ISubscriptionAccessPolicy` returning effective plan version, access mode, reason, grace deadline, scheduled change, and permitted recovery action.
- Model application access as `Full`, `Grace`, `ReadOnly`, or `FreeFallback`; never infer it independently in individual Services.
- Apply global defaults from JSON for grace duration, cancellation behavior, downgrade behavior, and after-grace action.
- Store per-plan trial duration and customer-specific grace extensions in the RDBMS.
- Never delete data because a subscription changed state.

#### Lifecycle behavior

- `Trialing`: paid-plan access until the projected trial end; warn before expiry.
- `Active`: pinned paid-plan access.
- `CancelAtPeriodEnd`: paid access with a visible end date and reactivation action.
- `PastDue` within grace: configured access with payment-failure banner and Portal action.
- after grace: read-only or Free fallback according to global policy; preserve data.
- `Paused`, `Unpaid`, or `Canceled`: apply configured fallback and expose recovery when Stripe permits it.
- upgrades are immediate with configured proration; downgrades default to period-end scheduling.

#### APIs, jobs, and UI

- Add lifecycle policy to billing/dashboard responses.
- Add commands for cancel-at-period-end, reactivate, and plan-change scheduling only where the Customer Portal does not already provide the configured behavior.
- Add `EvaluateSubscriptionLifecycleCommand` after every relevant webhook and as a recurring safety-net job.
- Show one consistent status banner across authenticated pages.
- Add an operator timeline containing Stripe events, local transitions, grace changes, and reconciliation results.

#### Tests and acceptance

- Use `TimeProvider` for boundary tests at trial end, period end, grace end, and scheduled plan changes.
- Test duplicate/out-of-order webhooks, recovery after payment, cancellation undo, Free fallback, read-only behavior, and customer override precedence.
- Acceptance: every Stripe subscription state maps to one deterministic local access decision, and replay/reconciliation converges to the same result.

### 18.10 Phase 13 — Transactional notifications

#### Outcome

Lifecycle, security, membership, and quota events produce reliable, retryable notifications without embedding email delivery inside request handlers.

#### Configuration and data

- Global provider, sender identity, template defaults, support address, retry policy, and notification feature switches live in JSON/secrets.
- Per-workspace and per-user delivery preferences live in the RDBMS.
- Add `NotificationPreference`, `NotificationDelivery`, and `NotificationSuppression` records.
- Delivery records contain template key, recipient identity, channel, safe metadata, status, attempts, provider ID, next attempt, and timestamps; do not store sensitive rendered bodies unnecessarily.
- Start with email and in-app notifications. Define an interface for future webhook/Slack channels without implementing them initially.

#### Events and jobs

- Publish notification intents for invitation, trial ending, payment failure/recovery, subscription change, scheduled cancellation, quota threshold, quota exhaustion, API-key creation/revocation, export ready, and destructive lifecycle completion.
- Enqueue `SendNotificationCommand` through ServiceStack Background Jobs.
- Deduplicate by `(WorkspaceId, EventId, TemplateKey, RecipientId, Channel)`.
- Add bounded retries and expose permanently failed delivery to operators.
- Schedule trial and quota warnings from persisted state, not browser activity.

#### UI and developer experience

- Add customer notification preferences and an in-app notification inbox.
- Add operator delivery search, failure details, retry, and suppressed-recipient visibility.
- Ship development capture/preview so templates can be tested without an external email provider.
- Keep templates source-controlled and brand-aware; global delivery behavior remains configuration-driven.

#### Tests and acceptance

- Test deduplication, retries, preference/suppression behavior, recipient authorization, template escaping, and no-secret logging.
- Acceptance: retrying the originating webhook or job never sends the same notification twice, and failed delivery never rolls back the business transaction.

### 18.11 Phase 14 — General audit trail

#### Outcome

Customers and platform operators can answer who changed what, when, from where, and why for security-sensitive operations.

#### Model and write path

- Generalize or supplement `SaasAuditEvent` with append-only `AuditEvent` records.
- Store workspace ID when tenant-scoped; actor user/service/API-key identity; action key; target type/opaque ID; UTC timestamp; request/correlation ID; IP/user agent where appropriate; safe before/after summaries; reason; and outcome.
- Define a registry of stable action keys rather than logging arbitrary prose.
- Centralize writes in `IAuditWriter`; audit failure for critical admin mutations should fail closed or use a transactional outbox.
- Redact secrets, tokens, raw API keys, Stripe payloads, file content, and sensitive personal data.
- Record denied cross-tenant and privileged access attempts in a platform security stream.

#### Coverage

- membership, ownership, and role changes;
- plan publication and subscriber migration;
- customer overrides and quota corrections;
- Checkout/Portal initiation and local subscription transitions;
- coupon administration;
- API-key lifecycle;
- file upload/download/delete metadata events;
- export/deletion/lifecycle requests;
- support access and impersonation;
- webhook replay and reconciliation actions.

#### APIs and UI

- `QueryWorkspaceAuditEvents` for Owner/Admin with plan entitlement `audit.read` where configured;
- `QueryPlatformAuditEvents` for authorized platform operators;
- searchable action, actor, target, outcome, and date filters;
- detail drawer with correlation links to request logs, jobs, Stripe inbox, or usage events;
- CSV export via Background Job for large ranges.

#### Tests and acceptance

- Test workspace isolation, role restrictions, redaction, append-only behavior, failed actions, platform/tenant visibility, and export authorization.
- Acceptance: every privileged state transition in this plan produces one traceable audit event with no secrets.

### 18.12 Phase 15 — Support and operations tooling

#### Outcome

A small SaaS team can diagnose and safely resolve routine customer problems from the built-in administration surfaces.

#### Operator capabilities

- unified workspace search by name, slug, billing email, user email, Stripe customer/subscription ID, and API-key fingerprint;
- customer 360 view: members, subscription/access state, plan, entitlements, quotas, recent usage, files, notifications, jobs, webhooks, audit history, and support notes;
- reconcile subscription, replay accepted webhook, retry failed job/notification/file deletion, expire override, and issue compensating usage adjustment;
- suspend/reactivate workspace access independently of Stripe, requiring reason and audit;
- private `SupportNote` records with author and timestamps;
- health views for Stripe configuration, DB, storage, job queues, webhook lag, notification provider, and analytics watermark.

#### Support access

- Do not implement silent or indefinite impersonation.
- Add optional `SupportAccessGrant` with workspace, operator, requested capability, reason, approval, start, maximum expiry, and revocation.
- Default support access is read-only and visibly indicated in the UI.
- Any “view as customer” session is short-lived, cannot access billing secrets or reveal API keys, and writes a start/end audit event.
- Allow deployments to disable support access globally in JSON.

#### API and UI

- Use AutoQuery for indexed read-only searches and custom Services for every operational mutation.
- Add role capabilities for `Admin`, `BillingAdmin`, and `Support`; default deny each new action.
- Require typed confirmation and reason for risky actions.
- Show dry-run impact for reconciliation, plan migration, bulk retry, and adjustment where possible.

#### Tests and acceptance

- Test the full role/action matrix, support-grant expiry, inability to reveal credentials, reason requirements, audit completeness, and cross-workspace protection.
- Acceptance: Support can diagnose common failures without database access, while billing and destructive actions remain unavailable without the corresponding capability.

### 18.13 Phase 16 — Data lifecycle and self-service

#### Outcome

Workspace owners can export, transfer, leave, cancel, and delete safely, while operators retain an auditable, retryable lifecycle process.

#### Configuration and models

- Global retention defaults, deletion delay, export expiry, legal-hold capability switch, and maximum export size live in JSON.
- Customer-specific retention exceptions and legal holds live in the RDBMS with reason and audit.
- Add `WorkspaceLifecycleRequest` with type (`Export`, `Delete`, `CancelDelete`, `TransferOwnership`), state, requester, approval/confirmation, scheduled time, progress, error, and completion details.
- Add `DataExportArtifact` with opaque storage key, expiry, checksum, and download audit fields.

#### Workflows

- Export: authorize Owner/Admin, capture a consistent manifest, build JSON/CSV plus stored files with a Background Job, store the artifact, notify the requester, and expire it automatically.
- Delete: require Owner confirmation and recent authentication, mark the workspace `PendingDeletion`, revoke sessions/API keys, stop new writes, wait the configured recovery window, then run idempotent staged deletion.
- Staged deletion covers files, database product data, caches, exports, queued jobs, notifications, and provider-side resources owned by the application.
- Preserve only the minimal billing/audit records required by configured policy; do not claim deletion from immutable backups beyond the documented retention schedule.
- Ownership transfer requires an active target member and cannot leave a workspace without an Owner.
- A sole Owner cannot leave until ownership is transferred or the workspace is deleted.

#### UI and operations

- Add export, transfer, leave, and delete actions to workspace settings with clear impact and recovery dates.
- Add operator progress, retry, legal-hold block, and failed-stage diagnostics.
- Add `ProcessWorkspaceLifecycleCommand`, `BuildWorkspaceExportCommand`, `DeleteWorkspaceDataCommand`, and `ExpireExportArtifactsCommand`.

#### Tests and acceptance

- Test authorization, recent-auth requirement, cancellation during recovery window, legal hold, job retries at every stage, file cleanup, API-key revocation, ownership invariants, and export isolation.
- Acceptance: replaying any lifecycle job is safe, deletion cannot cross workspace boundaries, and an exported archive contains exactly the documented customer-owned data.

### 18.14 Phase 17 — Template configuration and AI-agent ergonomics

#### Outcome

An adopter or coding agent can understand, rebrand, configure, extend, test, and deploy the template without discovering hidden policy scattered through the codebase.

#### Configuration

- Add a `Product` JSON section for organization name, product name, description, support/contact URLs, logo assets, legal URLs, and public-site terminology.
- Retain design tokens in CSS; document the small set of tokens required for brand colors, type, radius, and logo treatment.
- Keep meter and feature registries in `Saas` JSON.
- Keep plan versions, quotas, features, prices, and per-customer exceptions in the RDBMS.
- Provide strongly typed, validated configuration classes and a read-only redacted Admin view.
- Fail startup only for invalid settings needed by enabled features. The Free/local path must continue without Stripe or email credentials.

#### Repository organization

- Add `ARCHITECTURE.md`, `BILLING.md`, `ENTITLEMENTS.md`, `QUOTAS.md`, `TENANCY.md`, `OPERATIONS.md`, and a concise customization guide.
- Document each state machine, configuration owner, extension interface, Background Job, table category, and generated-code boundary.
- Keep one reference vertical slice showing DTO → Service → domain manager → OrmLite → generated TypeScript → React UI → tests.
- Add architecture decision records for static export, local Stripe projection, immutable usage ledger, and storage abstraction.
- Add a machine-readable feature manifest listing module, routes, tables, configuration, meters, entitlements, jobs, and tests.

#### Setup and verification

- Add a deterministic development reset/seed command that recreates SQLite, local file storage, sample users, Acme plans, and representative analytics data.
- The reset command must require an explicit development environment and exact configured paths; it must never infer or delete a broad directory.
- Add `.env.example`/configuration examples with placeholders only.
- Add Stripe CLI test fixtures, fake email capture, and sample API scripts.
- Add a single verification command that runs .NET build/tests, DTO freshness validation, TypeScript check, frontend tests, static production build, migration-from-empty test, and secret/generated-data checks.
- CI runs SQLite on every change and PostgreSQL for isolation/concurrency suites.

#### Acceptance

- Rebrand Acme and replace the sample meters using documented configuration and named assets without searching the entire repository for customer-visible strings.
- A fresh clone with no SQLite databases or local file directory recreates itself and passes the documented smoke flow.
- Generated TypeScript DTO drift and unclassified tenant tables fail CI.
- A coding agent can locate the correct file and test recipe for each platform concern from `AGENTS.md` and the feature manifest.

### 18.15 Cross-cutting API additions

The exact DTOs may be grouped where a cohesive response avoids client waterfalls, but the authoritative ServiceStack surface should cover:

#### Customer APIs

- `GetEffectiveEntitlements`;
- `GetUsageOverview`, `QueryUsageSeries`, `QueryUsageBreakdown`, `ExportUsageCsv`;
- `QueryStoredFiles`, `UploadStoredFile`, `DownloadStoredFile`, `DeleteStoredFile`;
- `GetNotificationPreferences`, `UpdateNotificationPreferences`, `QueryNotifications`, `MarkNotificationRead`;
- `QueryWorkspaceAuditEvents` and `ExportWorkspaceAuditEvents`;
- `CreateWorkspaceExport`, `GetWorkspaceLifecycleRequest`, `RequestWorkspaceDeletion`, `CancelWorkspaceDeletion`, `TransferWorkspaceOwnership`, `LeaveWorkspace`.

#### Platform APIs

- `GetSaasAnalytics`, `QuerySaasMetricSeries`, `RebuildSaasAnalytics`;
- customer 360 summary and cross-linked operational queries;
- subscription reconcile and lifecycle reevaluation;
- usage correction and reservation inspection;
- notification retry and delivery inspection;
- audit search/export;
- workspace suspend/reactivate;
- support note and temporary support-access management;
- lifecycle progress/retry and legal-hold management;
- redacted effective configuration and dependency health.

Large exports and rebuilds return a Background Job or lifecycle-operation ID. The client polls a bounded status endpoint or follows ServiceStack job state; it never holds an HTTP request open for long-running work.

### 18.16 Cross-cutting UI additions

Customer navigation after the roadmap:

- Dashboard;
- Files (Acme example module);
- Usage and analytics;
- Billing;
- Team;
- API keys;
- Audit log when entitled;
- Notifications;
- Settings and data lifecycle.

Platform administration after the roadmap:

- Overview and SaaS analytics;
- Plans, features, quotas, prices, coupons, and trials;
- Customers and customer 360;
- Subscriptions and lifecycle;
- Usage, reservations, corrections, and quota pressure;
- Stripe events and reconciliation;
- Jobs and notification deliveries;
- Audit and support access;
- Workspace lifecycle operations;
- read-only system policy and health.

All pages use the existing enterprise design system, responsive tables/cards, accessible dialogs and forms, URL-addressable filters, consistent loading/error/empty states, and generated DTO clients. No page may introduce a Next.js server runtime dependency.

### 18.17 Observability and health

Add structured metrics and health indicators for:

- entitlement denials by feature and plan;
- quota consumption, reservations, rejection, correction, and threshold latency;
- storage operations, orphan cleanup, and DB/object mismatches;
- analytics rollup/snapshot watermark and duration;
- subscription state transitions and reconciliation drift;
- notification queue, attempts, failure, and delivery latency;
- audit write failure;
- support-access grants currently active;
- lifecycle request duration and failed stage.

Every log and metric carries server-verified workspace context when tenant-scoped, but excludes content, secrets, tokens, raw API keys, and unnecessary personal data. Health endpoints distinguish liveness from readiness and do not expose detailed errors anonymously.

### 18.18 Release gates and end-to-end scenarios

Before declaring the post-foundation roadmap complete, automate these representative flows:

1. **Free signup:** register, receive a personal workspace, inspect entitlements and quotas, upload within limits, and view analytics.
2. **Quota enforcement:** fill document or byte capacity, observe warning and rejection, delete a file, and regain capacity.
3. **Paid conversion:** start trial/Checkout, process duplicate and out-of-order webhooks, receive paid entitlements, and retain an audit trail.
4. **Delinquency and recovery:** simulate payment failure, enter grace, restrict access after grace, recover payment, and restore access without data loss.
5. **Customer administration:** invite members, change roles, inspect per-user usage, export audit/usage data, and transfer ownership.
6. **Operator support:** find a workspace, diagnose an entitlement/quota issue, reconcile it, add an expiring override, and verify every action is audited.
7. **Data lifecycle:** export a workspace, request deletion, cancel within the recovery window, request again, and complete idempotent deletion.
8. **Isolation:** attempt each customer, file, analytics, export, job, cache, and API-key operation from another workspace and prove denial.
9. **Empty-state recreation:** remove only the documented development SQLite/files directories, start the application, migrate/seed automatically, and complete the Free smoke flow.
10. **Template customization:** change product configuration, feature registry, meters, and seed plans; regenerate DTOs and complete the build without editing generated code.

### 18.19 Recommended delivery slices

To keep each merge deployable and reviewable, implement the phases as these bounded slices:

1. entitlement resolver, API filter, client hook, plan matrix, and precedence tests;
2. workspace context, authorization helpers, table classification, and cross-tenant suite;
3. meter schema, reservation lifecycle, gauge adjustments, and concurrency tests;
4. local file store, upload/download/delete APIs, and `/documents` UI;
5. customer rollups, usage APIs/charts, warnings, and CSV export;
6. platform snapshots, SaaS analytics UI, and drill-downs;
7. lifecycle policy service, banners, transition jobs, and time-boundary tests;
8. notification outbox/jobs, email development capture, preferences, and operator delivery UI;
9. audit writer, coverage of privileged actions, customer/operator audit UI, and export;
10. customer 360, support roles/actions, notes, and optional temporary support access;
11. export, ownership, leave, deletion/recovery, retention, and lifecycle operations UI;
12. product configuration, documentation set, reset/seed tooling, manifest, and unified verification command.

Each slice must leave migrations runnable from an empty database. During template development migrations may be consolidated because backward compatibility is not required; before a derived product ships, published migrations become append-only.

## 19. Initial non-goals

- physical-product commerce;
- marketplaces or Stripe Connect;
- custom card collection or storage;
- application-generated tax invoices;
- arbitrary workflow/rules scripting from Admin UI;
- real-time multi-region quota counters;
- prepaid credit wallets or automatic top-ups;
- outcome-based billing;
- revenue recognition or accounting-ledger replacement;
- built-in customer-facing SSO/SAML implementation (the entitlement key and extension point may exist);
- document parsing, OCR, folder synchronization, web crawling, indexing, semantic search, RAG, or AI assistant implementation;
- reseller and channel billing.

The model leaves room for these features, but including them in the starter would make the common path harder to understand and maintain.

## 20. Research basis

- Next.js static exports generate an HTML file per route and support client-side data fetching, but omit features requiring a runtime server: <https://nextjs.org/docs/pages/guides/static-exports>
- Stripe supports flat-rate, per-seat, tiered, and usage-based recurring models: <https://docs.stripe.com/products-prices/pricing-models>
- Stripe recommends hosted Checkout, subscription webhooks, and the Customer Portal for a low-code subscription integration: <https://docs.stripe.com/billing/subscriptions/build-subscriptions>
- Stripe webhook delivery is asynchronous, retryable, and not guaranteed to be ordered: <https://docs.stripe.com/webhooks>
- Stripe meter summaries are updated asynchronously, so application-local counters are required for real-time quota enforcement: <https://docs.stripe.com/billing/subscriptions/usage-based/recording-usage>
- Stripe identifies hybrid subscription plus usage billing as an increasingly common SaaS model: <https://stripe.com/resources/more/usage-based-pricing-for-saas-how-to-make-the-most-of-this-pricing-model>
- Stripe documents automated invoice collection and retry policy for failed subscription payments: <https://docs.stripe.com/invoicing/automatic-collection>
- ServiceStack Admin UI exposes capability-specific operational tools: <https://docs.servicestack.net/admin-ui>
- ServiceStack AutoQuery supplies authorized schema-driven CRUD/admin experiences from typed contracts: <https://docs.servicestack.net/autoquery-schema>
- OWASP recommends server-verified tenant context, tenant-scoped storage/jobs/rate limits, and explicit tenant-isolation tests: <https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html>
- OWASP recommends deny-by-default authorization, per-request validation, appropriate security logging, and authorization tests: <https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html>
