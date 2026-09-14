# Authorization and roles

Next SaaS separates customer organization permissions from platform operator permissions. A role in one system never implies a role in the other.

[Security](README.md) · [Support operations](../features/support-operations.md)

## Organization roles

| Role | Typical authority |
| --- | --- |
| Owner | Full organization administration, ownership transfer, lifecycle/deletion |
| Admin | Profile, members, exports, and organization administration |
| Billing | Checkout, subscription, invoices/portal, and billing workflows |
| Member | Use entitled product features |

`WorkspaceAuthorization.RequireOwner`, `RequireAdmin`, and `RequireBilling` are the central policy helpers. Owner and Admin may administer; Owner, Admin, and Billing may manage billing.

Membership must be active and match the resolved organization. Ordinary member role changes cannot manufacture an Owner or bypass the ownership-transfer flow.

## Platform roles and capabilities

| Role | Capabilities |
| --- | --- |
| Admin | View customers, manage billing/support/platform, approve support access |
| BillingAdmin | View customers and manage billing |
| Support | View customers and manage support within support-access policy |

`PlatformAuthorization` maps roles to explicit capabilities. Use it for product-specific operator APIs even when a DTO also declares `[ValidateHasRole]`. This produces one reviewable role matrix and avoids accidental broadening as the console grows.

Platform membership does not create organization membership. Support users need an approved, started grant for redacted customer context; Admin has direct platform authority and does not impersonate a member.

## Enforcement order

For a customer operation:

1. authenticate the user or API credential;
2. resolve the organization and active membership;
3. evaluate workspace/subscription access mode;
4. require the relevant organization role;
5. require commercial feature entitlement;
6. enforce quota/idempotency;
7. constrain the resource query to the organization;
8. audit privileged results.

For platform operations, require the narrow platform capability and apply redaction/support-access constraints before loading customer details.

## Review rules

- UI visibility is not authorization.
- Avoid scattered string-role checks outside central policy.
- Default new actions to no role until deliberately assigned.
- Require recent authentication or exact confirmation for destructive operations.
- Test every allowed and denied role, not only anonymous access.
- Do not authorize from a plan name, price, email domain, or client-supplied organization ID.

## Related documentation

- [Tenant isolation](tenant-isolation.md)
- [Support access](support-access.md)
- [Add a ServiceStack API](../development/add-a-servicestack-api.md)

