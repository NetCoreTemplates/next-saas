# Database migrations

Next SaaS uses EF Core for ASP.NET Core Identity and ServiceStack OrmLite migrations for SaaS product data. Both use the configured database.

[Development recipes](README.md) · [Architecture](../concepts/architecture.md)

## Product schema with OrmLite

Product entities live in `MyApp.ServiceModel`; numbered migrations live in `MyApp/Migrations` and inherit `MigrationBase`.

```csharp
public class Migration1002 : MigrationBase
{
    public override void Up()
    {
        Db.CreateTable<Widget>();
    }

    public override void Down()
    {
        Db.DropTable<Widget>();
    }
}
```

Use a new migration number in any derived application whose database has been shared, deployed, or contains data that matters. Keep `Up` and `Down` deterministic and avoid network/provider calls.

During active template development, the repository intentionally supports reshaping `Migration1001` and recreating SQLite from empty state. That shortcut must not carry into a shipped customer application.

## Identity schema with EF Core

`ApplicationDbContext` owns users, roles, logins, and Identity tokens. Use EF migrations for Identity model changes. Do not map SaaS product entities into EF merely because Identity already uses it.

SQLite uses checked-in EF migrations when present. PostgreSQL currently bootstraps Identity from the current model with `EnsureCreated`, matching the template's clean-state development policy. A production-derived product should adopt a deliberate EF migration strategy before preserving PostgreSQL data across model changes.

## Run and inspect

```bash
cd MyApp
npm run migrate

# Development-only OrmLite helpers
npm run revert:last
npm run rerun:last
```

An empty database also bootstraps automatically when `Database.AutoMigrateEmpty` is enabled.

For a complete local reset:

```bash
ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes
```

This deletes the local SQLite database and local file store, then recreates users, roles, plans, and product tables. It is intentionally guarded and destructive.

## Data and seed rules

- `MyApp/plans.json` is an empty-state catalog seed, not ongoing catalog synchronization.
- Use migrations for deterministic reference data required by code.

![Initial Database Migration and Plan Seed](../assets/plan-seed.png)
- Use the Admin draft/publish workflow for normal commercial plan changes.
- Give tenant tables indexed `WorkspaceId` fields and enforce business uniqueness in the database.
- Test SQLite and PostgreSQL-specific behavior when changing concurrency, constraints, or SQL.

## Verify

Run migrations on an empty database and a database at the prior version, inspect constraints/indexes, run `Down` only in disposable development state, and execute `./scripts/verify.sh`. Its final gate starts production mode against a fresh temporary database and confirms reference tables and plans exist.

## Related documentation

- [Testing](testing.md)
- [Plans, pricing, and trials](../features/plans-pricing-trials.md)
- [Verify and ship](../getting-started/07-verify-and-ship.md)

