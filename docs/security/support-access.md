# Support access

Temporary support access limits how a platform operator can inspect a customer's organization during an investigation.

[Security](README.md) · [Support operations](../features/support-operations.md)

## Default posture

`Saas.EnableSupportAccess` is false by default. When disabled, grants cannot be created. Enabling the feature should be a deliberate deployment decision with operator MFA, monitoring, and customer policy.

## Grant lifecycle

1. An Admin selects a named Identity user already holding the Support role.
2. The Admin grants one organization, a required reason, and a duration capped by `Saas.SupportAccessMaxMinutes`.
3. The Support operator explicitly starts their own grant.
4. Customer 360 at `/admin/customers` resolves the exact operator, organization, start/end, expiry, revoke state, and `ReadOnly` capability; active grants are summarized at `/admin/security`.
5. The operator ends access, an Admin revokes it, or it expires automatically.

Admin operators already have direct platform capability and do not start support sessions. BillingAdmin authority remains limited to billing/customer diagnostics.

![Support Access Four-Stage Lifecycle](../assets/support-access-lifecycle.png)

## Data minimization

Support projections remove billing identifiers unless the role can manage billing. Raw API keys, provider secrets, file bytes, authentication factors, and customer export contents are not support data.

Support access is read-only. Product mutation endpoints still require organization membership or a separate platform capability; a support grant must not be treated as impersonation.

![Customer 360 Platform Inspection](../assets/customer-360.png)

Grant, start, denied use, end, revoke, expiry-sensitive access, notes, and associated operator actions should be correlated through audit and request IDs. Reasons and notes must not contain credentials or unnecessary personal data.

## Production controls

- Require phishing-resistant MFA where possible for Admin and Support.
- Use least-privilege named accounts; prohibit shared operators.
- Alert on unusually long, frequent, cross-region, or after-hours sessions.
- Review active grants and operator roles regularly.
- Define whether/how customers are notified and how support actions appear in their audit view.
- Remove access immediately during offboarding.

## Verify

Test globally disabled access, wrong operator/tenant, unstarted grant, expiry boundary, revoke/end, Admin behavior, BillingAdmin denial, read-only mutation attempts, redacted fields, notes, audit coverage, and operator offboarding.

## Related documentation

- [Authorization and roles](authorization-and-roles.md)
- [Data protection and privacy](data-protection-and-privacy.md)
- [Audit logs](../features/audit-logs.md)
