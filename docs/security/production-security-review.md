# Production security review

Use this checklist as the final template-to-product review. It is not a certification and must be adapted to the product's data, jurisdictions, customers, integrations, and hosting model.

[Security](README.md) · [Production deployment](../operations/deployment.md)

## Identity and sessions

- [ ] Real SMTP/domain delivery works for confirmation and recovery.
- [ ] Password, lockout, cookie, session lifetime, and enumeration policy is explicit.
- [ ] MFA is required for platform operators and evaluated for customer admins.
- [ ] The included authentication limit is tuned, distributed where needed, and connected to abuse alerts.
- [ ] External identity providers validate issuer, redirect URIs, claims, and account linking.
- [ ] The included coordinated personal-account deletion behavior matches product retention requirements.
- [ ] Development users/passwords cannot be seeded in Production.

## Authorization and tenants

- [ ] Every mutation has a documented organization or platform permission.
- [ ] Every tenant-owned query includes resolved `WorkspaceId`.
- [ ] Cross-tenant list/read/write/delete/export/job tests exist.
- [ ] Feature gates and quotas are enforced server-side.
- [ ] Platform projections are role-filtered and support access is off unless intentionally enabled.
- [ ] Destructive operations require recent proof/confirmation, reason, and audit.

## Credentials and providers

- [ ] Secrets are outside Git/build output and rotation is tested.
- [ ] API-key database/backups are treated as credential-bearing; key rotation/revocation works.
- [ ] Distributed/API gateway limits replace the in-memory limiter when scaling.
- [ ] Stripe keys, objects, and webhook endpoint use the intended account/mode.
- [ ] Webhook raw-body signature, duplicate delivery, replay, and reconciliation tests pass.
- [ ] SMTP, storage, database, and deployment credentials use least privilege.

## Web and application

- [ ] HTTPS, HSTS, restricted hosts, forwarded headers, and trusted proxy boundaries are correct.
- [ ] Cookie flags, Razor antiforgery, and included same-origin SaaS mutation checks are tested.
- [ ] The included CSP, frame, content-type, referrer, opener, and permissions headers are deployed and tightened for this product.
- [ ] CORS is absent or narrowly allow-listed; credentialed wildcard CORS is impossible.
- [ ] Input, pagination, body-size, timeout, and queue-admission limits exist.
- [ ] Upload scanning/content validation and safe download/preview behavior match accepted formats.
- [ ] Dependencies and container/base images are scanned, patched, and reproducibly built.

## Data and operations

- [ ] Data inventory, processors, residency, retention, export, and deletion policy is approved.
- [ ] Database/object storage/backups/key rings are encrypted and access-reviewed.
- [ ] Backup restore and provider-side-effect replay drills meet RPO/RTO.
- [ ] Logs, request logs, audit, metrics, and support systems avoid credentials/customer content.
- [ ] Legal hold and lifecycle behavior is tested at worker execution time.
- [ ] `/ready`, failed queues, billing, retention, auth anomalies, and infrastructure have actionable alerts.
- [ ] Incident response names owners, evidence sources, containment, notification, and credential rotation.

## Release evidence

Record the reviewed revision, environment, non-secret configuration digest, migrations, dependency scan, penetration/security-test results, backup drill date, exceptions with owners/expiry, and final approvers.

![Production Verification and Preflight Gates](../assets/release-gates.png)

Run the executable baseline:

```bash
./scripts/preflight.sh
```

Then complete the product-specific manual and automated checks above.

## Related documentation

- [Testing](../development/testing.md)
- [Observability and health](../operations/observability-and-health.md)
- [Data protection and privacy](data-protection-and-privacy.md)
