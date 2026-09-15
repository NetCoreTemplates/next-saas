# 1. Understand the template

Next SaaS provides the generic foundation that most self-service B2B SaaS products need. You replace the Acme example domain while retaining the reusable identity, billing, tenancy, entitlement, quota, and operations infrastructure.

[Documentation home](../README.md) · [Next: local setup](02-local-setup.md)

![Next SaaS Landing Page](../assets/landing-hero.png)

## What is included

- Public product and pricing pages, registration, sign-in, and themed account management.
- Multiple organizations per user with Owner, Admin, Billing, and Member roles.
- Immutable plan versions with monthly and annual prices, features, quotas, and trials.
- Stripe Checkout, Customer Portal, coupons, promotion codes, catalog provisioning, and signed webhooks.
- Local subscription projections so authorization does not depend on a live Stripe request.
- Idempotent usage events, quota reservations, counters, gauges, warnings, and analytics.
- Organization-scoped API keys, files, members, audit events, and notifications.
- Data export, ownership transfer, delayed organization deletion, retention, and legal holds.
- Admin, BillingAdmin, and Support operator roles with time-boxed read-only support access.
- ServiceStack Background Jobs for durable asynchronous work.
- A static Next.js frontend served by the ASP.NET Core application in production.
- Production startup validation, health checks, request correlation, tests, and deployment preflight.

## Technology choices

| Layer | Technology | Role |
| --- | --- | --- |
| Runtime | .NET 10 and ASP.NET Core | Hosting, Identity, configuration, and middleware |
| APIs | ServiceStack | Typed services, validation, API keys, OpenAPI, Admin UI, and jobs |
| Data | OrmLite and EF Core Identity | SaaS records and user identity |
| Database | SQLite locally; PostgreSQL in production | Persistent enforcement and customer state |
| Billing | Stripe Billing | Checkout, subscriptions, invoices, trials, and discounts |
| Frontend | React 19, Next.js 16, TypeScript | Customer, marketing, and operator experiences |
| Styling | Tailwind CSS | Shared responsive visual system |

These are conventional, strongly typed technologies with extensive documentation and predictable code organization. Both human developers and coding agents can trace a feature from its DTO through its service and generated browser client.

## The Acme example

Acme offers secure document storage and usage analytics. Its files are deliberately basic. They exist to demonstrate:

- tenant-isolated data;
- document-count and exact-byte storage quotas;
- reserve, stream, settle, and compensate behavior;
- asynchronous deletion;
- customer analytics and lifecycle exports.

Do not build your product around document ingestion unless that is your actual domain. Replace or remove the example pages and services while preserving the generic patterns they demonstrate.

## Configuration boundaries

The template deliberately separates three kinds of decisions:

1. **Deployment-wide behavior** belongs in JSON configuration, such as enabled currencies, retention defaults, trial policy, and quota warnings.
2. **Plan behavior** belongs in the RDBMS and is managed from the **Plans** tab at `/admin/plans`, such as prices, quotas, features, and trial duration.
3. **Customer-specific behavior** belongs in the RDBMS with an actor and reason, such as negotiated entitlements, retention exceptions, and legal holds.

This separation keeps global policy reviewable while allowing safe runtime administration.

![Configuration Ownership](../assets/configuration-ownership.png)

## Important invariants

- Every customer-owned query and mutation is constrained to an organization.
- A subscription points to a published plan version; editing a plan does not silently change existing contracts.
- Stripe is authoritative for payment state, while the local database is authoritative for request-time access checks.
- Usage and external events require stable idempotency keys.
- Subscription changes never delete customer data.
- Long-running and retryable work belongs in Background Jobs.
- Raw API keys, Stripe secrets, passwords, and support-only data are never returned to inappropriate roles.
- Generated TypeScript DTOs are replaced, not hand-edited.

## What you should customize first

Most adopters should change these areas first:

1. Product identity and public content.
2. Features and quota meters.
3. Default plan catalog.
4. The example document module.
5. Stripe sandbox configuration.
6. Production database, email, domains, secrets, and retention policy.

The rest of this onboarding path walks through that sequence.

[Next: run the template locally →](02-local-setup.md)
