# Testing

Tests should concentrate on SaaS policy boundaries: authorization, organization isolation, idempotency, quota edges, immutable contracts, external-event replay, and recovery.

[Development recipes](README.md) · [Verify and ship](../getting-started/07-verify-and-ship.md)

## Backend tests

Run all .NET tests from the repository root:

```bash
dotnet test MyApp.slnx
```

Use fast in-memory SQLite tests for policy and persistence behavior. Seed only the minimum organization, membership, plan version, subscription, quota, and period needed for the scenario. Assert stable HTTP error codes as well as status.

For concurrency-sensitive aggregate changes, set `NEXT_SAAS_TEST_POSTGRES` to an administrative PostgreSQL connection. The integration fixture creates and drops an isolated schema and is skipped when the variable is absent.

High-value backend cases include:

- unauthenticated, wrong-role, and cross-tenant access;
- first operation, idempotent replay, exact quota, and over-limit rejection;
- reservation settle, release, expiry, and concurrent admission;
- plan-version pinning and override precedence;
- webhook signature rejection, duplicate inbox events, and replay;
- support-access expiry/redaction and lifecycle legal hold;
- audit redaction and retention boundaries.

`ArchitectureGuardTests` protects template-wide invariants that are easy to accidentally weaken.

## Frontend tests

From `MyApp.Client`:

```bash
npm run typecheck
npm run test:run
npm run build
```

Vitest and Testing Library cover hooks, feature gates, and complex admin components. Prefer observable behavior over implementation details. Mock the typed client boundary and test loading, empty, error, success, mutation refresh, and entitlement states.

When testing effects, assert the request count as well as rendered output. This catches unstable dependency loops that otherwise appear as flickering pages and repeated APIs.

## Full repository gate

```bash
./scripts/verify.sh
```

This builds and tests .NET, type-checks/tests/builds React, then starts production mode against a fresh temporary SQLite database and verifies empty-state bootstrap. Run it before handing off any cross-cutting change.

## Test-data safety

Never point destructive/reset tests at a non-disposable database. Keep secrets out of snapshots and captured request output. Clean generated databases and local object-store files before committing.

## Related documentation

- [Database migrations](database-migrations.md)
- [Add a meter and quota](add-a-meter-and-quota.md)
- [AI-assisted development](ai-assisted-development.md)

