# Deployment configuration profiles

[DATABASE.md](../DATABASE.md) is the step-by-step guide for choosing a provider and configuring
it locally and in production. This file is the reference behind it.

These templates make the database choice a configuration decision rather than a code change.
The same choice applies locally and in production: `DB_PROVIDER` selects the Kamal destination
that deploys, and `./scripts/dev-db.sh up` starts that same provider on the developer's machine,
so nobody develops against a database they do not ship. SQLite is the default so a first
deployment needs no external service; switching providers is a one-variable change.

## How the provider switch works

The application already supports every provider at runtime through `Database:Provider`. The
deployment layer selects one with a [Kamal destination](https://kamal-deploy.org):

| Layer | SQLite | PostgreSQL | MySQL | SQL Server |
| --- | --- | --- | --- | --- |
| Kamal overlay | `deploy.sqlite.yml` | `deploy.postgres.yml` | `deploy.mysql.yml` | `deploy.sqlserver.yml` |
| Kamal secrets | `.kamal/secrets.sqlite` | `.kamal/secrets.postgres` | `.kamal/secrets.mysql` | `.kamal/secrets.sqlserver` |
| Accessory image | none | `postgres:18-alpine` | `mysql:8.4` | `mssql/server:2022-latest` |
| Creates DB and app login | n/a | `init.sh` initializer | image env vars | `pre-deploy.sh` via sqlcmd |
| Production JSON | `appsettings.deploy.sqlite.example.json` | `…postgres…` | `…mysql…` | `…sqlserver…` |
| Local container | none | `./scripts/dev-db.sh up` | same | same |

Every server provider takes the same single `DB_PASSWORD` secret and connects as an
unprivileged login that owns only its own database. SQL Server additionally enforces a
password policy — at least eight characters from three of uppercase, lowercase, digits, and
symbols — so a hex password is rejected; `configure-deployment.sh` validates this before
the container can fail to start. SQL Server also needs roughly 2GB of memory.

`config/deploy.yml` and `.kamal/secrets-common` hold everything shared by all providers, and
Kamal deep-merges the selected overlay over them. Each overlay must parse as a YAML mapping, so a
provider with nothing to override still declares `accessories: {}`. The Release workflow reads the `DB_PROVIDER`
repository variable (default `sqlite`) and passes `-d "$DB_PROVIDER"` to every `kamal` command.

## Local parity

`./scripts/dev-db.sh up` runs the provider named by `DB_PROVIDER` from the same image as its
accessory, creates the same `next_saas` database and unprivileged `next_saas` login using the
same initializers (`config/db/postgres/init.sh`, `config/db/sqlserver/init.sql`), and writes the
local connection into the private `.env` the application reads in Development, leaving
`MyApp/appsettings.Development.json` on the SQLite default a new clone starts from. Only the host
and the password differ from the deployment; the local password is fixed, local-only, and
overridable with `DEV_DB_PASSWORD`, while `DB_PASSWORD` stays a deployment secret.

`./scripts/reset-dev.sh --yes` follows the local provider: it deletes the SQLite file, or
recreates the container's data volume, then migrates and reseeds.

A provider added below should gain its local case in `scripts/dev-db.sh` at the same time, or
developers silently fall back to a different engine than the one deployed.

## SQLite validation

Use `appsettings.deploy.sqlite.example.json` to verify a first single-host deployment, domain,
TLS, persistent volume, empty-state creation, login, files, jobs, and application APIs before
provisioning external services.

SQLite requires one application instance and a durable `App_Data` mount. SMTP and Stripe are
deliberately optional in this profile.

## PostgreSQL production

Use `appsettings.deploy.postgres.example.json` for production operation. It requires PostgreSQL,
explicit release migrations, SMTP, Stripe, and the Stripe webhook.

## Workflow

```bash
cp config/appsettings.deploy.sqlite.example.json MyApp/appsettings.Production.json
# Customize every example value.
./scripts/configure-deployment.sh \
  --provider sqlite \
  --service my-app \
  --repo owner/my-app \
  --set-github-secrets
```

`configure-deployment.sh` applies the provider's database policy to the JSON, runs
`preflight.sh`, and uploads `APPSETTINGS_JSON`, the `DB_PROVIDER` variable, and `DB_PASSWORD`.
A provider that runs a database server needs only that one password secret; `.kamal/secrets.postgres`
maps it to both the application login and the `POSTGRES_PASSWORD` the postgres image requires.
Both scripts read `.env` like `config/deploy.yml` does, so `DB_PROVIDER` and `DB_PASSWORD` set
there are picked up and `--provider` defaults to `$DB_PROVIDER`. Run it again with `--provider postgres` to switch; nothing else in the JSON is
touched.

The local configuration filename is gitignored.

The optional `BootstrapAdmin` section creates or promotes the first platform administrator during
migration. Replace its example email and password before the first release. After signing in
successfully, remove `BootstrapAdmin` from the JSON file, upload the secret again, and redeploy;
the administrator remains in the database while the bootstrap password no longer remains in
deployment configuration.

Changing the provider against an empty deployment is configuration-only. Changing it after
customer data exists requires a separately planned data migration; application schema migrations
do not copy data between providers.

## Adding another provider

Adding MySQL or a managed database is additive and needs no workflow change:

1. add its OrmLite and EF Core packages and a `Database:Provider` branch in `MyApp/Configure.Db.cs`;
2. add `config/deploy.<provider>.yml` and `.kamal/secrets.<provider>`;
3. add `config/db/<provider>/pre-deploy.sh` if it needs an accessory booted;
4. add a `config/appsettings.deploy.<provider>.example.json` profile and a branch in
   `scripts/configure-deployment.sh`;
5. add its image, connection string, readiness probe, and client shell to `scripts/dev-db.sh`
   so it runs locally too, and list it in `DATABASE.md`;
6. set the `DB_PROVIDER` repository variable to the new name.

For a managed database hosted elsewhere, steps 2 and 3 reduce to an overlay with no accessory:
only the connection string in the production JSON changes.
