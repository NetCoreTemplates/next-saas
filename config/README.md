# Deployment configuration profiles

These templates make the database choice a configuration decision rather than a code change.

## SQLite validation

Use `appsettings.deploy.sqlite.example.json` to verify a first single-host deployment, domain, TLS, persistent volume, empty-state creation, login, files, jobs, and application APIs before provisioning external services.

SQLite requires one application instance and a durable `App_Data` mount. SMTP and Stripe are deliberately optional in this profile.

## PostgreSQL production

Use `appsettings.deploy.postgres.example.json` for production operation. It requires PostgreSQL, explicit release migrations, SMTP, Stripe, and the Stripe webhook.

## Workflow

```bash
cp config/appsettings.deploy.sqlite.example.json MyApp/appsettings.Production.json
# Customize every example value.
./scripts/preflight.sh --json MyApp/appsettings.Production.json --config-only
gh secret set APPSETTINGS_JSON < MyApp/appsettings.Production.json
```

The local configuration filename is gitignored. Replace the source profile with the PostgreSQL example when that is the intended target.

The optional `BootstrapAdmin` section creates or promotes the first platform administrator during migration. Replace its example email and password before the first release. After signing in successfully, remove `BootstrapAdmin` from the JSON file, upload the secret again, and redeploy; the administrator remains in the database while the bootstrap password no longer remains in deployment configuration.

Changing the provider against an empty deployment is configuration-only. Changing it after customer data exists requires a separately planned data migration; application schema migrations do not copy data between providers.
