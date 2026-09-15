# Tenant isolation

The internal `Workspace` is the tenant boundary shown to customers as an Organization. Files, usage, subscription state, entitlements, members, notifications, audit, credentials, and lifecycle records are organization-bound.

[Security](README.md) · [Organizations and tenancy](../concepts/organizations-and-tenancy.md)

## Context resolution

`WorkspaceContextResolver` returns the organization, active membership, and acting user.

For an interactive cookie session it resolves the active `UserWorkspacePreference`, verifies an active membership, and loads a non-deleted organization. For API-key requests it ignores browser preference and resolves the organization stored on that credential, then verifies the key owner still has active membership.

Never accept a customer-provided `WorkspaceId` as authorization. IDs in routes and payloads identify candidate resources only after the caller's tenant context is known.

## Query pattern

Customer-resource queries include the tenant predicate:

```csharp
var widget = Db.Single<Widget>(x =>
    x.Id == request.Id &&
    x.WorkspaceId == context.Workspace.Id)
    ?? throw new HttpError(404, "WidgetNotFound", "The widget was not found.");
```

Return `404` for inaccessible resource IDs where distinguishing “exists elsewhere” would leak tenant information. Include indexed `WorkspaceId` on tenant-owned tables and use composite unique constraints for organization-scoped business keys.

![Tenant-Scoped Query Pattern](../assets/scoped-query.png)

## Storage, jobs, and analytics

- File object keys are opaque and metadata is tenant-bound; physical paths are never accepted from users.
- Background-job payloads use durable identifiers, then re-read and revalidate organization state.
- Usage idempotency keys are unique within an organization.
- Analytics dimensions are controlled by meter configuration and do not create cross-tenant groupings.
- Exports and downloads verify tenant ownership again when retrieved.

## Platform access

Customer 360 is an explicit platform path with capability filtering and redacted projections. Support access is time-bound to one operator and one organization. Do not reuse platform queries in customer APIs or expose full persistence models when a redacted projection exists.

## Testing strategy

For every new tenant-owned resource:

1. create organizations A and B;
2. give one user different roles in each;
3. create resources with overlapping names/business keys;
4. attempt list, read, update, delete, export, job, and API-key access across tenants;
5. test inactive membership and deleted/suspended organization state;
6. inspect error bodies, timing, audit, and logs for information leakage.

![Tenant Isolation and Cross-Tenant Rejection Test](../assets/isolation-test.png)

## Related documentation

- [Authorization and roles](authorization-and-roles.md)
- [API credentials and abuse controls](api-credentials-and-abuse-controls.md)
- [Testing](../development/testing.md)

