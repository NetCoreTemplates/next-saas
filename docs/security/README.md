# Security

Security in Next SaaS is layered across ASP.NET Core Identity, ServiceStack request validation, organization context, role/capability policy, entitlements, quotas, audit, and deployment checks.

[Documentation home](../README.md)

## Guides

| Area | Guide |
| --- | --- |
| Login, confirmation, 2FA, invitations, and account lifecycle | [Authentication and accounts](authentication-and-accounts.md) |
| Organization and platform permissions | [Authorization and roles](authorization-and-roles.md) |
| Preventing cross-customer access | [Tenant isolation](tenant-isolation.md) |
| API keys, scopes, quotas, and rate limiting | [API credentials and abuse controls](api-credentials-and-abuse-controls.md) |
| Signed provider events and safe replay | [Stripe webhook security](stripe-webhook-security.md) |
| Time-limited operator access | [Support access](support-access.md) |
| Protected keys, logs, audit, export, and deletion | [Data protection and privacy](data-protection-and-privacy.md) |
| Browser, request, upload, and response boundaries | [Web and input security](web-and-input-security.md) |
| Final threat-led release review | [Production security review](production-security-review.md) |

## Security invariants

- The browser is never an authorization boundary.
- Every customer-owned lookup is constrained by resolved organization context.
- Organization roles and platform roles are separate.
- Stripe and SMTP secrets never enter the frontend bundle.
- Published plan state and local subscription projections drive access without live provider calls.
- Raw credentials and customer content do not belong in logs or audit metadata.
- Retried external and background work preserves its original idempotency boundary.
- Production readiness checks are a baseline, not a complete security assessment.

Perform a product-specific threat model before launch. Authentication methods, data sensitivity, compliance obligations, integrations, and hosting architecture can materially change the required controls.

