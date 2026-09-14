# AI-assisted development

The template is organized so coding agents can follow explicit contracts, stable extension seams, small configuration files, and deterministic validation commands.

[Development recipes](README.md) · [Project tour](../getting-started/03-project-tour.md)

## Give an agent the right context

Start with `AGENTS.md`, this docs hub, and the smallest relevant concept/feature guide. State the customer behavior and policy boundary, not only the desired component or database field.

A useful request includes:

- who can perform the operation;
- whether it is global, per plan, or per organization;
- feature and quota behavior;
- external provider ownership;
- expected failure/retry behavior;
- UI states and verification scenarios.

Ask the agent to inspect existing patterns before editing. File names and request types are more reliable anchors than broad framework instructions.

## Preserve machine-readable sources of truth

- C# ServiceStack DTOs own API contracts.
- `appsettings.json` owns global deployment policy.
- the RDBMS owns plans and customer-specific state.
- `plans.json` seeds only an empty database.
- generated `MyApp.Client/lib/dtos.ts` is output, not an editing surface.
- `./scripts/verify.sh` is the final executable acceptance gate.

When an agent changes a contract, require DTO regeneration. When it changes persistent state, require empty-state migration validation. When it changes a hook, require request-count coverage.

## Scope work in vertical slices

Prefer one complete capability—contract, authorization, persistence, UI, and tests—over many disconnected stubs. Keep provider integrations behind interfaces and use fake gateways in policy tests.

For reviewable results, ask for:

1. the user-visible outcome;
2. material design decisions;
3. files changed;
4. commands and test counts;
5. remaining risks or intentionally deferred work.

## Guardrails for generated and local state

Agents should preserve unrelated worktree changes, use migrations or explicit development resets appropriately, and never commit secrets, SQLite databases, uploaded files, build output, or data-protection keys.

Do not let an agent infer authorization from UI visibility, a browser-supplied organization ID, or a Stripe response on every request. Direct it back to `WorkspaceContextResolver`, local subscription projections, and effective entitlements.

## Prompt example

```text
Add organization-bound widget creation for Owner and Admin members.
Gate it with widgets.basic and enforce a billing-period widgets.created quota.
The database stores customer-specific widgets; global behavior stays in Saas config.
Use reserve/settle because the provider call can fail, enqueue the provider work with
ServiceStack Background Jobs, generate the TypeScript DTOs, add an AppShell page,
and test cross-tenant access, idempotent replay, exact/over limit, retry, and static build.
Run ./scripts/verify.sh and report the results.
```

## Related documentation

- [Add a ServiceStack API](add-a-servicestack-api.md)
- [Testing](testing.md)
- [Architecture](../concepts/architecture.md)

