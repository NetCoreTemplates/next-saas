# Configuration

Next SaaS uses ASP.NET Core configuration. Checked-in JSON provides safe development defaults; environment-specific values override it with double-underscore keys.

[Operations](README.md) · [Customize the product](../getting-started/04-customize-the-product.md)

## Ownership rules

| Scope | Storage | Examples |
| --- | --- | --- |
| Deployment-wide behavior | JSON/environment | URLs, currencies, retention defaults, feature registry |
| Secrets | secret manager/environment | Stripe secret, webhook secret, SMTP password, DB password |
| Commercial plan state | RDBMS | prices, trials, features, quotas, Stripe Price IDs |
| Organization exception | RDBMS | entitlement override, retention policy, legal hold |

The Admin UI may display global capabilities but does not mutate global JSON. This keeps infrastructure policy reviewable and avoids settings that differ silently between application instances.

![Configuration Ownership: Global, Plan, and Customer Tiers](../assets/configuration-ownership.png)

## Main sections

- `AppConfig` — canonical public base URL.
- `AllowedHosts` — accepted host header values.
- `Database` and `ConnectionStrings` — provider, migration behavior, connection.
- `Deployment` — fail-closed production requirements.
- `Security` — response headers, cookie-API same-origin policy, and authentication throttling.
- `Product` — deployment-wide name, description, contacts, and legal links.
- `FileStorage` — local implementation root and upload safety limits.
- `Notifications` and `SmtpConfig` — delivery mode and sender/provider settings.
- `Saas` — global lifecycle, access, retention, feature, and meter policy.
- `Stripe` — client/server keys, webhook secret, portal, and catalog provisioning policy.

See `.env.example` for environment names without real credentials, or start with the SQLite/PostgreSQL profiles in `config/` when deploying through Kamal.

The `Security` section provides `EnableSecurityHeaders`, `RequireSameOriginForCookieApi`, `AuthenticationRequestsPerMinute`, and the production `ContentSecurityPolicy`. Keep the two enforcement switches enabled unless an equivalent upstream control is implemented and tested.

## Environment overrides

Nested JSON keys use `__`:

```bash
AppConfig__BaseUrl=https://saas.example.com
Database__Provider=PostgreSql
Database__AutoMigrateEmpty=false
ConnectionStrings__DefaultConnection='Host=db;Database=acme;Username=acme;Password=...'
Notifications__Provider=Smtp
Stripe__SecretKey=sk_live_...
```

Set `ASPNETCORE_ENVIRONMENT=Production` explicitly. Behind a trusted reverse proxy, enable forwarded headers so HTTPS redirects and generated URLs use the public scheme.

## Production policy

`Deployment.EnforceStartupChecks` runs `ProductionReadiness` before traffic is served. Defaults require HTTPS, restricted hosts, PostgreSQL, SMTP, Stripe with verified webhooks, non-test Stripe keys, and explicit migrations.

Relax an individual `Deployment` requirement only when the product genuinely does not need it or for a deliberate single-host validation—for example SQLite before PostgreSQL, or a free-only internal service. `preflight.sh` honors the same `Deployment.RequirePostgreSql`, `RequireExplicitMigrations`, `RequireSmtp`, `RequireStripe`, and `RequireStripeWebhook` switches as runtime startup validation and emits warnings for reduced modes. Treat every override as a recorded architecture decision, not a way around preflight.

## Validate

```bash
./scripts/doctor.sh
./scripts/preflight.sh --config-only
./scripts/preflight.sh --json MyApp/appsettings.Production.json --config-only
```

`doctor.sh` validates local tools and development configuration. `preflight.sh` accepts either exported production environment variables or the same JSON bundle supplied to Kamal, and does not print secret values.

## Change management

- Review global policy changes like code.
- Apply the same values to every instance.
- Deploy feature/meter registry additions before publishing plans that reference them.
- Use additive/compatible configuration during rolling deployments.
- Record the effective non-secret configuration with each release.

## Related documentation

- [Secrets](secrets.md)
- [Deployment](deployment.md)
- [Plans, pricing, and trials](../features/plans-pricing-trials.md)
