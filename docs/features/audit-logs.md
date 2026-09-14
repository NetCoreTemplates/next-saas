# Audit logs

The SaaS audit trail records security, billing, lifecycle, support, and administrative decisions with actor and tenant context.

[Documentation home](../README.md) · [Support operations](support-operations.md)

## Customer and platform views

| Audience | Routes |
| --- | --- |
| Organization | `GET /saas/audit`, `GET /saas/audit/export` |
| Platform operators | `GET /saas/admin/audit`, `GET /saas/admin/audit/export` |

Organization audit access requires the `audit.read` entitlement. Platform search supports organization, category, action, outcome, date range, and free text, with authorization enforced by the service.

Organization members use `/audit`. Platform Admin users search and export the cross-organization audit stream from `/admin/security`.

## Event shape

`SaasAuditEvent` contains organization, category, action, outcome, actor, subject, request correlation, network/client context, timestamp, and sanitized metadata. Use registered, stable action names so filters, exports, alerts, and downstream integrations do not depend on prose.

## Recording events

Record an event at the point a meaningful decision commits: successful and denied privileged changes, billing transitions, credential management, lifecycle requests, support access, and operator actions. Include enough identifiers to investigate the event without copying entire request bodies.

The audit helper redacts known sensitive keys and credential-like patterns and limits metadata size. Redaction is a backstop, not permission to pass secrets. Never audit passwords, raw API keys, Stripe secrets, invitation tokens, export contents, or uploaded file bytes.

## Retention and export

`Saas.AuditRetentionDays` is the global default; organization retention policy and legal hold can change cleanup behavior. CSV exports apply the same tenant and role filters as the list APIs.

Audit history supports product investigation but is not automatically a regulated write-once ledger. Products with formal immutability requirements should stream events to appropriately controlled external storage.

## Adding an audited operation

1. choose an existing category or register a deliberate new one;
2. use a stable past-tense action;
3. record organization, actor, subject, outcome, and request ID;
4. add only allow-listed, sanitized metadata;
5. test success, denial, and cross-tenant behavior;
6. include the new event in relevant retention/export reviews.

## Verify

Exercise each privileged flow, inspect customer and platform views, export filtered CSV, test redaction with credential-shaped values, verify pagination and tenant isolation, and run retention with and without legal hold.

## Related documentation

- [API keys](api-keys.md)
- [Data lifecycle](data-lifecycle.md)
- [Support operations](support-operations.md)
