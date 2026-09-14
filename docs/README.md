# Next SaaS documentation

Next SaaS is a .NET 10, ServiceStack, ASP.NET Core Identity, React 19, and Next.js 16 template for multi-tenant B2B SaaS products. Acme, the included document-storage product, is intentionally small: it demonstrates subscriptions, quotas, storage, analytics, teams, and operations without imposing a product domain.

## Start here

New developers should follow the onboarding path in order. It takes the application from a clean checkout to a verified product change and optional Stripe sandbox subscription.

1. [Understand the template](getting-started/01-overview.md)
2. [Run it locally](getting-started/02-local-setup.md)
3. [Tour the project](getting-started/03-project-tour.md)
4. [Customize the product](getting-started/04-customize-the-product.md)
5. [Add a metered feature](getting-started/05-add-a-metered-feature.md)
6. [Connect Stripe sandbox](getting-started/06-connect-stripe-sandbox.md)
7. [Verify and ship](getting-started/07-verify-and-ship.md)

## Reference documentation

The remaining documentation is organized by task and can be read independently:

- **Concepts** — [architecture](concepts/architecture.md), [organizations and tenancy](concepts/organizations-and-tenancy.md), [plans and entitlements](concepts/plans-and-entitlements.md), [usage and quotas](concepts/usage-and-quotas.md), and [background processing](concepts/background-processing.md).
- **Features** — [billing and subscriptions](features/billing-and-subscriptions.md), [plans, pricing, and trials](features/plans-pricing-trials.md), [coupons](features/coupons.md), [organizations and members](features/organizations-and-members.md), [API keys](features/api-keys.md), [file storage](features/file-storage.md), [usage analytics](features/usage-analytics.md), [notifications](features/notifications.md), [audit logs](features/audit-logs.md), [data lifecycle](features/data-lifecycle.md), and the [Operations Center and support workflows](features/support-operations.md).
- **Development** — [recipe index](development/README.md), [ServiceStack APIs](development/add-a-servicestack-api.md), [feature gates](development/add-a-feature-gate.md), [meters and quotas](development/add-a-meter-and-quota.md), [background jobs](development/add-a-background-job.md), [database migrations](development/database-migrations.md), [frontend pages](development/add-a-frontend-page.md), [typed DTO generation](development/generate-typed-dtos.md), [testing](development/testing.md), and [AI-assisted development](development/ai-assisted-development.md).
- **Operations** — [runbook index](operations/README.md), [configuration](operations/configuration.md), [secrets](operations/secrets.md), [deployment](operations/deployment.md), [database and storage](operations/database-and-storage.md), [observability and health](operations/observability-and-health.md), [background jobs and recovery](operations/background-jobs-and-recovery.md), [external services](operations/external-services.md), [retention and lifecycle](operations/retention-and-lifecycle.md), [backup and restore](operations/backup-and-restore.md), and [troubleshooting](operations/troubleshooting.md).
- **Security** — [security index](security/README.md), [authentication and accounts](security/authentication-and-accounts.md), [authorization and roles](security/authorization-and-roles.md), [tenant isolation](security/tenant-isolation.md), [API credentials and abuse controls](security/api-credentials-and-abuse-controls.md), [Stripe webhook security](security/stripe-webhook-security.md), [support access](security/support-access.md), [data protection and privacy](security/data-protection-and-privacy.md), [web and input security](security/web-and-input-security.md), and the [production security review](security/production-security-review.md).

The planned onboarding, concept, feature, development, operations, and security documentation sets are now present. Existing root-level documents remain available as compatibility entry points where material has moved into this structure.

## Documentation conventions

- Paths are relative to the repository root unless stated otherwise.
- `Workspace` is the internal code and database term; the product UI calls it an **Organization**.
- C# request and response types in `MyApp.ServiceModel` are authoritative. `MyApp.Client/lib/dtos.ts` is generated.
- Configuration keys use JSON notation in prose, such as `Saas.EnableTrials`, and double underscores in environment variables, such as `Saas__EnableTrials`.
- Commands assume a Bash-compatible shell on Linux or macOS. Equivalent PowerShell commands can be used on Windows.

## Quick command index

```bash
# Run locally
cd MyApp
dotnet watch

# Diagnose local configuration
./scripts/doctor.sh

# Recreate development state
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes

# Run every validation gate
./scripts/verify.sh

# Check production configuration and run verification
./scripts/preflight.sh
```

Useful application surfaces:

| URL | Purpose |
| --- | --- |
| `/` | Public product site |
| `/dashboard` | Customer organization overview |
| `/admin` | Operations Center overview and side navigation |
| `/admin/customers` | Customer 360 and customer exceptions |
| `/admin/plans` | Plans and Coupons tabs for catalog and discount management |
| `/admin/usage` | Platform usage analytics and quota pressure |
| `/admin/operations` | Failed work queues and integration readiness |
| `/admin/security` | Retention, support access, and platform audit |
| `/admin/settings` | Platform configuration ownership |
| `/admin-ui` | ServiceStack administration |
| `/scalar/v1` | OpenAPI reference |
| `/ui` | ServiceStack API Explorer |
| `/up` | Process liveness |
| `/ready` | Database and file-store readiness |
