# Add a ServiceStack API

Define contracts in C#, implement policy in the service layer, then regenerate the TypeScript client.

[Development recipes](README.md) · [Architecture](../concepts/architecture.md)

## 1. Define the contract

Add request, response, and public data types to `MyApp.ServiceModel`. Use an explicit route, HTTP marker interface, typed return, declarative validation, and authentication requirement.

```csharp
[ValidateIsAuthenticated]
[RequiresFeature("widgets.basic")]
[Route("/saas/widgets", "POST")]
public class CreateWidget : IPost, IReturn<CreateWidgetResponse>
{
    [ValidateNotEmpty]
    public string Name { get; set; } = "";

    [ValidateNotEmpty]
    public string IdempotencyKey { get; set; } = "";
}

public class CreateWidgetResponse
{
    public string Id { get; set; } = "";
    public ResponseStatus? ResponseStatus { get; set; }
}
```

Use opaque public IDs and stable error codes. Do not expose OrmLite rows when a smaller response projection is sufficient.

## 2. Implement organization policy

Implement the operation in `MyApp.ServiceInterface`. Resolve the active organization on the server and include its ID in every resource lookup.

```csharp
public class WidgetServices(IWorkspaceContextResolver workspaceContexts) : Service
{
    public async Task<object> Any(CreateWidget request)
    {
        var session = await GetSessionAsync();
        var context = workspaceContexts.Resolve(Db, session, Request);
        WorkspaceAuthorization.RequireAdmin(context); // only when needed

        var existing = Db.Single<Widget>(x =>
            x.WorkspaceId == context.Workspace.Id &&
            x.IdempotencyKey == request.IdempotencyKey);
        if (existing != null)
            return new CreateWidgetResponse { Id = existing.Id };

        var widget = new Widget {
            Id = Guid.NewGuid().ToString("N"),
            WorkspaceId = context.Workspace.Id,
            Name = request.Name.Trim(),
            IdempotencyKey = request.IdempotencyKey,
            CreatedDate = DateTime.UtcNow,
        };
        Db.Insert(widget);
        return new CreateWidgetResponse { Id = widget.Id };
    }
}
```

Follow existing service base/context patterns rather than trusting a `WorkspaceId` sent by a customer. API-key calls resolve their organization from the credential binding.

## 3. Cover product invariants

- Apply `RequiresFeature` for commercially controlled capability.
- Use `WorkspaceAuthorization` for organization roles and `PlatformAuthorization` for staff roles.
- Add a unique database constraint for idempotency or business uniqueness.
- Meter accepted work through `ISaasManager` when it consumes quota.
- Record a registered audit event for privileged or security-relevant changes.
- Enqueue slow or retryable external work only after durable state exists.

## 4. Generate and consume the client

Run the application, regenerate `MyApp.Client/lib/dtos.ts`, and call the request class through the shared `JsonServiceClient`.

```tsx
const api = await client.api(new CreateWidget({
  name,
  idempotencyKey: crypto.randomUUID(),
}))

if (!api.succeeded)
  setError(api.error?.message ?? 'Unable to create widget.')
```

Never hand-edit generated DTOs or reproduce secret-dependent decisions in React.

## 5. Verify

Test unauthenticated access, missing entitlement, every allowed/denied role, validation, idempotent replay, cross-organization identifiers, read-only subscription access, and the successful typed client call. Finish with `./scripts/verify.sh`.

## Related documentation

- [Add a feature gate](add-a-feature-gate.md)
- [Add a meter and quota](add-a-meter-and-quota.md)
- [Generate typed DTOs](generate-typed-dtos.md)
