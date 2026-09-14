# Database and storage

The RDBMS is authoritative for Identity and SaaS metadata. `IFileStore` is authoritative for uploaded/exported bytes, linked by opaque object keys.

[Operations](README.md) · [File storage](../features/file-storage.md)

## Database providers

SQLite is the frictionless local default and allows the whole template to recreate from an empty file. It is also supported for a first single-host deployment check when the durable `App_Data` volume is mounted. The default production policy requires PostgreSQL.

`Configure.Db.cs` gives EF Core Identity and OrmLite product data the same connection. Keep their ownership separate even though they share the database:

- EF Core: users, roles, logins, Identity tokens;
- OrmLite: organizations, subscriptions, plans, usage, files, audit, lifecycle, operations.

The repository includes ready-to-customize deployment profiles:

- `config/appsettings.deploy.sqlite.example.json` — one instance, durable volume, automatic empty-state creation;
- `config/appsettings.deploy.postgres.example.json` — PostgreSQL, explicit release migrations, SMTP, and Stripe.

Both providers use the same application code. Set PostgreSQL explicitly with JSON configuration or equivalent environment variables:

```bash
Database__Provider=PostgreSql
Database__AutoMigrateEmpty=false
ConnectionStrings__DefaultConnection='Host=db;Database=acme;Username=acme;Password=...'
```

Grant the runtime account only required application privileges. Decide whether migrations use the same account or a separately controlled role.

## Moving from SQLite to PostgreSQL

For a disposable deployment-path test, discard the SQLite data, provision an empty PostgreSQL database, replace the deployment configuration, and redeploy. The migration task creates the current schema and reference data.

For retained customer data, changing `Database.Provider` is not a data migration. Back up both stores, stop writes, transform and import Identity plus SaaS tables, reconcile row counts and quotas, and only then direct the application to PostgreSQL. Treat uploaded file bytes separately: their object keys remain in the database while the configured `IFileStore` remains authoritative for content.

## Connection and capacity operations

Monitor active connections, pool exhaustion, query latency, locks, deadlocks, storage growth, replication lag, and backup age. Quota admission relies on transactions and conditional updates, so database correctness and lock behavior matter.

Run maintenance and index analysis using PostgreSQL-native tooling. Avoid direct manual edits to subscription, aggregate, or lifecycle rows; use audited application workflows or a reviewed migration.

## File store

`LocalFileStore` writes under `FileStorage.RootPath` and is appropriate for development or one durable host. The Kamal example mounts `/opt/docker/{service}/App_Data` into the container.

For multiple instances or stronger durability, implement `IFileStore` with shared object storage. Preserve:

- opaque tenant-safe keys;
- streaming reads/writes;
- maximum-size enforcement and hashes;
- idempotent delete;
- readiness probing appropriate to the provider;
- lifecycle/export expiry cleanup.

Uploaded filenames and content types are untrusted metadata. Add malware/content scanning when the derived product serves customer uploads to other users.

## Operational SQLite files

ServiceStack jobs and request logs create SQLite files under `App_Data/jobs` and `App_Data/requests`. They are operational stores, separate from the main business database. Keep the job store durable if pending work must survive deployment; request logs may use shorter retention or an external diagnostic sink.

Do not copy live SQLite files without a database-aware snapshot method. WAL, database, and shared-memory state must be handled consistently.

## Related documentation

- [Backup and restore](backup-and-restore.md)
- [Background jobs and recovery](background-jobs-and-recovery.md)
- [Database migrations](../development/database-migrations.md)
