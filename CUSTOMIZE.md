# Customize the template

Customize the database first, in [DATABASE.md](DATABASE.md): set `DB_PROVIDER` in `.env` to the
provider you intend to deploy (`sqlite`, `postgres`, `mysql`, or `sqlserver`) and run
`./scripts/dev-db.sh up`, so development happens on the engine that ships and one variable also
configures production.

Then global identity and policy:

1. Update `Product`, `FileStorage`, `Notifications`, and `Saas` in `MyApp/appsettings.json`.
2. Replace Acme logo treatment and CSS tokens in the shared layouts.
3. Change seed plans in `MyApp/Migrations/Migration1001.cs`; during template development you may reset the local database with `ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes` and rerun migration.
4. Keep feature and meter keys stable once customers or usage exist.
5. Regenerate `MyApp.Client/lib/dtos.ts` after C# contract changes.
6. Run `./scripts/verify.sh`.

Public footer pages are ordinary Markdown files under `MyApp.Client/app/(content)`. Edit a page's `page.md` content directly, or copy one of the existing route folders to add another page. The shared `(content)/layout.tsx` supplies the Acme navigation, footer, responsive content surface, and light/dark typography automatically. Update `MyApp.Client/components/footer.tsx` when adding or renaming a footer link.

For a new billable resource, add its configured meter, plan allowances, a Service that resolves workspace/entitlement first, and either fixed idempotent consumption or reserve/settle/release. Add customer analytics and at least one quota/isolation test.

For cloud storage, implement `IFileStore` for S3, Cloudflare R2, or Azure Blob and register it in place of `LocalFileStore`. Preserve opaque keys, streaming, checksums, bounded input, idempotent delete, and tenant-scoped authorization.

The machine-readable [features.json](features.json) maps modules to routes, tables, policy, jobs, and tests so coding agents can find the correct extension point without repository-wide guesswork.
