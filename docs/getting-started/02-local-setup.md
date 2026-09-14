# 2. Run the template locally

This page takes a clean checkout to a working development application and explains how to return to a clean state.

[Previous: overview](01-overview.md) · [Documentation home](../README.md) · [Next: project tour](03-project-tour.md)

## Prerequisites

Install:

- .NET 10 SDK;
- Node.js 24 or a compatible current LTS release;
- npm;
- SQLite CLI for the diagnostic and verification scripts;
- Git.

Stripe CLI is optional until the Stripe sandbox step. PostgreSQL is only required for production and the optional integration test.

Confirm the local toolchain from the repository root:

```bash
./scripts/doctor.sh
```

Warnings about Stripe or email are expected for a new local checkout. Errors should be resolved before continuing.

## Install dependencies

Install the browser dependencies and the small Node toolchain used by the ASP.NET host:

```bash
cd MyApp.Client
npm install

cd ../MyApp
npm install
```

NuGet packages are restored automatically by `dotnet build` or `dotnet watch`.

## Start the application

```bash
cd MyApp
dotnet watch
```

Open `https://localhost:5001`. ASP.NET Core owns the public origin and starts the Next.js development process automatically. Browser API requests therefore use the same origin as Identity cookies and ServiceStack APIs.

The first Development start with no application database:

1. creates the ASP.NET Core Identity schema;
2. runs the OrmLite SaaS migrations;
3. seeds Free, Pro, Business, and Enterprise plans;
4. creates development users;
5. creates a personal organization and Free subscription when a seeded user first signs in.

## Development accounts

All seeded users use the password `p@55wOrd`.

| Account | Intended use |
| --- | --- |
| `admin@email.com` | Full SaaS administration |
| `billing@email.com` | BillingAdmin boundary testing |
| `support@email.com` | Support-access boundary testing |
| `manager@email.com` | Customer owner and team workflows |
| `employee@email.com` | Normal customer membership |
| `test@email.com` | Minimal authenticated flow |

Seed users are created only in Development. Production startup never creates known credentials.

## Check the application

After signing in, verify:

- `/dashboard` loads the active organization once without repeated API calls;
- `/documents` permits a small supported file upload;
- `/usage` displays document and storage usage;
- `/settings` displays organization lifecycle controls for an Owner;
- `/admin` opens the Operations Center for `admin@email.com`; its side menu links to the focused `/admin/*` routes;
- `/up` and `/ready` return healthy responses.

API contracts and diagnostic tools are available at `/scalar/v1`, `/ui`, and `/admin-ui`.

## Recreate local state

This template is designed to recreate itself from an empty development database. Stop the application, then run:

```bash
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes
```

The script validates exact paths before removing `MyApp/App_Data/app.db` and `MyApp/App_Data/files`. It then runs the migration task. It refuses to run outside Development or without `--yes`.

ServiceStack Background Jobs and request logs use separate generated SQLite databases under `MyApp/App_Data/jobs` and `MyApp/App_Data/requests`. They are operational development artifacts and can also be removed while the application is stopped.

## Common setup problems

### The HTTPS certificate is not trusted

Trust the ASP.NET Core development certificate:

```bash
dotnet dev-certs https --trust
```

### Port 5001 or 3000 is already in use

Stop the previous `dotnet watch`, `dotnet run`, or Next.js development process before starting another instance.

### The schema does not match the code

During template development, recreate the local database with `reset-dev.sh`. Derived production applications should add a forward migration instead.

### Paid checkout is unavailable

That is expected until Stripe sandbox keys and plan Price IDs are configured in step 6.

[Next: tour the project →](03-project-tour.md)
