# Choose your database

This is the first thing to customize. The database provider is a configuration choice rather
than a code fork, and the same choice applies in both places: `DB_PROVIDER` selects the engine
`scripts/dev-db.sh` runs on your machine *and* the Kamal destination the Release workflow
deploys. Setting it once means you develop against the database you ship.

Decide before you create data you care about. Switching providers against an empty deployment
is configuration-only; switching after customers exist needs a separately planned data
migration, because schema migrations do not copy data between engines.

## Which provider

| Provider | `DB_PROVIDER` | Choose it when | Local requirement |
| --- | --- | --- | --- |
| SQLite | `sqlite` | A single application instance is enough, and you want no external service to run or back up. The zero-dependency default. | none |
| PostgreSQL | `postgres` | The usual production choice: multiple application instances, strong concurrency, wide managed-hosting support. | Docker or Podman |
| MySQL | `mysql` | Your organization already operates MySQL or MariaDB. | Docker or Podman |
| SQL Server | `sqlserver` | Your organization standardizes on SQL Server. Budget roughly 2GB of memory for the container. | Docker or Podman |

Whichever you choose, the application code is identical: `MyApp/Configure.Db.cs` branches on
`Database:Provider` for both OrmLite and EF Core, and `DB_PROVIDER` supplies that setting, so
the name above is the only place a provider is written down.

## Step 1 — Record the choice

`.env` is gitignored and holds your local operator settings:

```bash
cp .env.example .env     # first time only
```

Set one line in it:

```bash
DB_PROVIDER=postgres     # sqlite, postgres, mysql, or sqlserver
```

That single variable is read by `scripts/dev-db.sh` (local), by
`scripts/configure-deployment.sh` (production JSON and GitHub secrets), and as the
`DB_PROVIDER` repository variable that the Release workflow passes to every `kamal` command as
its destination. Nothing else selects a database.

## Step 2 — Start the database locally

```bash
./scripts/dev-db.sh up
```

For a server provider this:

- starts the same image the deployment's Kamal accessory runs — `postgres:18-alpine`,
  `mysql:8.4`, or `mcr.microsoft.com/mssql/server:2022-latest`;
- creates the same `next_saas` database and the same unprivileged `next_saas` login, using the
  same initializers the deployment uses (`config/db/postgres/init.sh`,
  `config/db/sqlserver/init.sql`, or the MySQL image's own environment variables);
- publishes its usual port on `localhost` and keeps its data in a named container volume;
- writes the local connection into your private `.env`.

SQLite starts nothing; it writes the same file and `MyApp/App_Data/app.db` is created on first
run.

It sets `DB_PROVIDER` and `ConnectionStrings__DefaultConnection` in `.env`, replacing those
assignments in place and leaving the rest of the file alone. `DB_PROVIDER` implies
`Database:Provider` in the application, so the provider is one setting rather than two that can
drift apart. The application reads `.env`
when it runs in Development, so any `Key__Sub` value there overrides `MyApp/appsettings.json`
and `MyApp/appsettings.Development.json` on your machine only:

```text
.env                                 your private overrides, gitignored
MyApp/appsettings.Development.json   the SQLite default every clone starts from
MyApp/appsettings.json               deployment-wide template defaults
```

`MyApp/appsettings.Development.json` therefore stays on SQLite in source control, and switching
your own machine to PostgreSQL changes nothing a teammate has to review. A variable already set
in your environment still wins over `.env`, so a one-off `DB_PROVIDER=sqlite dotnet run` keeps
working, and an explicit `Database__Provider` wins over `DB_PROVIDER`. Nothing in `.env` reaches
a deployment.

Useful overrides, all optional:

| Variable | Effect |
| --- | --- |
| `DEV_DB_PASSWORD` | Local password. Defaults to `Dev_Passw0rd!Local`, which is deliberately fixed so a teammate reproduces the same local database. It is never a deployment credential. |
| `DEV_DB_PORT` | Published port, when 5432, 3306, or 1433 is already taken. |
| `DEV_DB_ENGINE` | The engine to run locally when the destination names a hosting arrangement rather than an engine — see [Managed databases](#managed-databases). |
| `DOCKER` | Container runtime to use. Otherwise `docker`, then `podman`. |

## Step 3 — Create the schema and sign in

```bash
cd MyApp
npm run migrate
```

The first normal start also detects an empty database and creates the Identity schema, the SaaS
schema, reference plans, and Development-only sample users. Existing databases are never
destructively recreated.

```bash
dotnet watch
```

Open `https://localhost:5001` and sign in as `admin@email.com` with `p@55wOrd`.

Check the wiring at any time:

```bash
./scripts/doctor.sh          # warns when the local engine and DB_PROVIDER disagree
./scripts/dev-db.sh status   # provider, container state, connection target
./scripts/dev-db.sh shell    # psql, mysql, or sqlcmd against the local database
```

To start over on empty data — whichever provider is running:

```bash
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes
```

## Step 4 — Configure production with the same provider

Start from the matching profile. It carries the database policy the provider implies:

```bash
cp config/appsettings.deploy.postgres.example.json MyApp/appsettings.Production.json
```

Customize every example value in it — base URL, allowed hosts, product identity, SMTP, Stripe,
and the `BootstrapAdmin` block that creates your first platform administrator. The filename is
gitignored.

Generate the one database secret. A provider that runs a database server takes exactly one
operator-managed password, used for both the application login and the image's own
administrative account:

```bash
export DB_PASSWORD="$(openssl rand -hex 32)"
```

SQL Server rejects that: its password policy requires at least eight characters drawn from three
of uppercase, lowercase, digits, and symbols, which a hex string does not satisfy. Use:

```bash
export DB_PASSWORD="$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 40)"
```

Then apply the provider to the production configuration and upload the deployment secrets:

```bash
./scripts/configure-deployment.sh \
  --service my-app \
  --repo owner/my-app \
  --set-github-secrets
```

`--provider` defaults to `$DB_PROVIDER`, so this is the same decision from Step 1. The script:

- writes `Database`, `ConnectionStrings`, and the `Deployment` policy for that provider into
  `MyApp/appsettings.Production.json`, backing up the previous file and leaving every other
  setting untouched;
- runs `scripts/preflight.sh --config-only` to validate the result without printing secrets;
- uploads `APPSETTINGS_JSON` and `DB_PASSWORD` as GitHub Actions secrets and sets the
  `DB_PROVIDER` repository variable.

Drop `--set-github-secrets` to review the JSON first; the script then prints what still needs to
be uploaded.

## Step 5 — Deploy and confirm

Run the full preflight, then release:

```bash
set -a; source .env.production; set +a
./scripts/preflight.sh
```

The Release workflow reads the `DB_PROVIDER` repository variable, runs
`config/db/$DB_PROVIDER/pre-deploy.sh` to boot or start the database accessory, and passes
`-d "$DB_PROVIDER"` to every `kamal` command. `config/deploy.<provider>.yml` deep-merges over
`config/deploy.yml`, and secrets resolve from `.kamal/secrets-common` plus
`.kamal/secrets.<provider>`.

After the release, `https://your-domain/ready` reports database and file-store readiness.

An empty database bootstraps itself on first start. Apply later migrations as a deliberate
release step, before starting multiple instances:

```bash
cd MyApp
dotnet run --no-launch-profile --AppTasks=migrate
```

## Switching providers later

Against an empty deployment, change `DB_PROVIDER` in `.env` and repeat Steps 2 and 4:

```bash
./scripts/dev-db.sh up
./scripts/configure-deployment.sh --service my-app --repo owner/my-app --set-github-secrets
```

`scripts/reset-kamal-deployment.sh` previews the exact service scope — containers, images,
accessories, app directories, and persistent state — before an explicit `--yes` reset, and
clears every provider's accessory rather than only the selected one, so a switch leaves a clean
slate. Local data for the previous provider stays in its own container volume until you remove
it with `./scripts/dev-db.sh reset --provider <previous>`.

With customer data in place, plan a data migration instead. Schema migrations create structure;
they do not move rows between engines.

## Managed databases

To deploy against RDS, Cloud SQL, Azure SQL, or any database you do not run yourself, add a
destination that provisions nothing:

1. create `config/deploy.<name>.yml` containing `accessories: {}`;
2. create `.kamal/secrets.<name>` with whatever the application needs;
3. add no `config/db/<name>/pre-deploy.sh`;
4. set `DB_PROVIDER=<name>` and put the managed connection string in
   `MyApp/appsettings.Production.json` yourself;
5. set `DEV_DB_ENGINE` to the engine it actually runs, so
   `./scripts/dev-db.sh up` still gives you the same engine locally;
6. set `Database__Provider` explicitly, because a destination named after a managed instance is
   not a provider name the application recognizes, so it cannot imply one.

For example, a managed PostgreSQL destination named `postgres-managed` uses
`DB_PROVIDER=postgres-managed` with `DEV_DB_ENGINE=postgres` and
`Database__Provider=PostgreSql`.

## Troubleshooting

**`Cannot talk to docker.`** The daemon is not running, or your user cannot reach its socket.
Start it, add yourself to the `docker` group (`sudo usermod -aG docker "$USER"`, then log in
again), or run the script with `sudo`; it hands `.env` back to you afterwards.

**`docker or podman is required to run the <provider> provider locally.`** Install a container
runtime, or set `DB_PROVIDER=sqlite` to develop without a database server.

**`Database.Provider is postgres but DefaultConnection is a SQLite connection string.`** The
provider and the connection string disagree. Startup fails deliberately rather than creating a
database nobody intended. Re-run `./scripts/dev-db.sh up` locally, or
`./scripts/configure-deployment.sh` for a deployment.

**VS Code cannot open `.env`, or the application cannot read it.** The file is owned by `root`,
because `./scripts/dev-db.sh` was run through `sudo`. Take it back with
`sudo chown "$USER" .env`. To avoid needing `sudo` at all, add yourself to
the `docker` group (`sudo usermod -aG docker "$USER"`, then log in again) or use rootless Podman;
the script now hands the file back to the invoking user when it does run under `sudo`.

**The port is already in use.** Another database is bound to it; set `DEV_DB_PORT` and re-run
`./scripts/dev-db.sh up`.

**`doctor.sh` warns that the local database and `DB_PROVIDER` disagree.** You changed
`DB_PROVIDER` without restarting the local database. Run `./scripts/dev-db.sh up`.

**The container exits at startup.** `./scripts/dev-db.sh up` prints the last lines of its log.
For SQL Server, the usual cause is a `DEV_DB_PASSWORD` that fails the complexity policy.

## Reference

- [config/README.md](config/README.md) — the provider matrix, the deployment profiles, and how
  to add another provider.
- [README.md](README.md#run-the-deployed-database-locally) — the short version of this guide.
- [AGENTS.md](AGENTS.md#database-providers) — the invariants a change to any of this must keep.
