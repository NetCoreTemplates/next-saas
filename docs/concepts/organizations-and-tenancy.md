# Organizations and tenancy

The UI calls a customer boundary an **Organization**. Internal DTOs and database records use the conventional `Workspace` name. They describe the same tenant boundary.

[Documentation home](../README.md) · [Architecture](architecture.md)

## Organization context

`IWorkspaceContextResolver` is the mandatory boundary for tenant-facing services. It starts with an authenticated identity, reloads active membership, and returns the verified `Workspace`, `WorkspaceMember`, user ID, and effective role.

A client-provided organization or resource ID only identifies what the caller wants. Authorization comes from persisted membership and an organization-scoped query.

```csharp
var context = workspaceContexts.Resolve(Db, await GetSessionAsync(), Request);
```

Product services should pass this resolved context into entitlement, access, quota, storage, and audit operations rather than reconstructing it.

## Active organization selection

One identity can belong to multiple organizations. `UserWorkspacePreference` stores the identity’s current selection.

- `GetMyWorkspaces` returns verified active memberships only.
- `SwitchWorkspace` rejects an organization outside that set.
- The application shell reloads organization state after an explicit switch.
- API keys do not follow the browser’s active preference; each key is permanently bound to its organization.

![Organization Switcher](../assets/organization-switcher.png)

If the preference is missing or invalid, the resolver selects an active membership using deterministic fallback rules and persists the selection.

## Customer roles

| Role | Typical permissions |
| --- | --- |
| Owner | All organization administration, ownership transfer, and destructive lifecycle actions |
| Admin | Members, settings, exports, and normal organization administration |
| Billing | Billing and subscription management without general administration |
| Member | Entitled product capabilities |

The Owner role has invariants that ordinary role editing cannot bypass:

- an organization must retain an Owner;
- an Owner transfers ownership through the explicit workflow;
- a sole Owner cannot leave;
- only the Owner can schedule or cancel organization deletion.

Role checks are centralized in `WorkspaceAuthorization`. UI visibility reflects permission but does not replace server checks.

![Team Roles and Responsibilities](../assets/team-roles.png)

## Platform roles are separate

Platform operators are not organization members by implication:

| Platform role | Scope |
| --- | --- |
| Admin | Catalog, platform policy, customer operations, platform audit, and support approval |
| BillingAdmin | Billing-safe customer lookup, reconciliation, and Stripe retries |
| Support | Redacted customer diagnostics during an approved, started, expiring session |

A Support role alone grants no customer access. `SupportAccessGrant` must match the exact operator and organization and must be active, started, unexpired, and not revoked or ended.

## Invitations

Invitations are persisted as pending `WorkspaceMember` records with:

- normalized invited email;
- random token represented by a one-way hash;
- explicit role;
- expiry and send/revoke dates.

Acceptance requires an authenticated Identity email matching the invitation. Expired, revoked, already active, or mismatched invitations are rejected. A pending invitation may also be promoted on a subsequent authenticated SaaS request after its email is verified.

## Tenant-owned queries

Every tenant record lookup must include the verified organization:

```csharp
var file = Db.Single<StoredFile>(x =>
    x.Id == request.Id &&
    x.WorkspaceId == context.Workspace.Id);
```

Avoid loading by primary key and checking later, particularly before returning data or acting on an object-store key. Apply the boundary in reads, updates, deletes, downloads, analytics, exports, caches, and job commands.

![Tenant Isolation and Cross-Tenant Rejection Test](../assets/isolation-test.png)

## API keys

ServiceStack API keys are bound using `RefIdStr = WorkspaceId`. Resolution requires:

- an active, non-revoked key;
- an explicit organization binding;
- an active membership for the key’s user in that organization;
- applicable feature and rate-limit policy.

Changing the user’s browser-selected organization does not change the API key. Raw keys are shown only once; later lists expose a safe visible fingerprint.

## Files and exports

Object keys contain opaque IDs rather than customer filenames:

```text
workspaces/{workspace-id}/files/{opaque-id}
workspaces/{workspace-id}/exports/{lifecycle-request-id}.zip
```

Original names remain metadata. Authorization loads the exact organization-owned row before opening or deleting its object. Export construction queries each included table by `WorkspaceId` and writes to an organization-specific object key.

## Background jobs

Job messages carry stable resource identifiers, not trusted serialized customer state. A worker reloads the row, its current organization, status, policy, and relevant provider state before acting.

This matters when access, a legal hold, deletion status, or membership changes while work is queued.

## Adding tenant-owned data

When adding a table:

1. Add a required, indexed `WorkspaceId` unless the ownership is indirect and rigorously enforced.
2. Classify the table as `tenantOwned` in `features.json`.
3. Scope every service query and mutation to the resolved organization.
4. Include it deliberately in export, retention, and deletion policy.
5. Avoid customer-provided object-store paths and cache keys.
6. Add cross-organization read, update, delete, download, and job tests.
7. Decide what platform Support, BillingAdmin, and Admin roles may see, including redaction.

## Common mistakes

- Treating an authenticated user ID as authorization for all of that user’s organizations.
- Accepting `WorkspaceId` from the body without reloading membership.
- Looking up a resource by ID alone.
- Reusing browser active-organization state for API keys.
- Giving platform Support permanent or implicit customer access.
- Omitting new customer data from export and deletion workflows.

## Related concepts

- [Architecture](architecture.md)
- [Plans and entitlements](plans-and-entitlements.md)
- [Usage and quotas](usage-and-quotas.md)
- [Background processing](background-processing.md)

