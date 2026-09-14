# Data protection and privacy

Protect customer data across transport, storage, logs, exports, support access, backups, retention, and deletion—not only in the primary database.

[Security](README.md) · [Data lifecycle](../features/data-lifecycle.md)

## Transport and protected application state

Production defaults require an HTTPS public URL, redirect HTTP to HTTPS, and enable HSTS. Terminate TLS only at trusted infrastructure and configure forwarded headers correctly.

ASP.NET Core data-protection keys are persisted under `App_Data`. Store the key ring on durable protected storage, restrict access, back it up, and share it safely across instances. Losing it can invalidate cookies/tokens; disclosure can weaken protected data.

Use database, disk/object-store, backup, and secret-manager encryption appropriate to the deployment. The template does not automatically configure provider-side encryption or key rotation.

## Logs and audit

Request-body capture is Development-only, and Stripe webhook bodies are excluded. Production logs should avoid query strings/headers/bodies containing tokens or customer content.

`SaasAudit` registers accepted action names, requires tenant context for customer categories, limits field sizes, and redacts sensitive property names and recognizable Stripe/API-key patterns. Redaction is defense in depth; callers must still supply allow-listed metadata rather than arbitrary request objects.

Restrict and retain request logs, application logs, audit exports, and support tickets according to their sensitivity.

## Files and exports

Opaque object keys separate physical storage from customer filenames. Tenant authorization is rechecked on downloads. Extensions and content types are not proof of safe content; add malware scanning, content validation, safe download headers, and isolated preview/conversion for the derived product.

Organization export archives contain concentrated customer data. Authenticate every download, expire bytes, avoid public object URLs, log access, and include new customer-owned tables deliberately.

## Retention and deletion

Global retention defaults can be overridden per organization, and legal hold prevents cleanup/deletion. Organization deletion is delayed, cancelable, password-confirmed, audited, and separate from user-account deletion.

Deletion/anonymization requirements for backups, Stripe, email providers, telemetry, support systems, and legal records need a product-specific policy. Immediate removal from an active system may coexist with time-limited protected backup retention.

## Data inventory

Before launch, classify each field/object by owner, sensitivity, purpose, residency, retention, access roles, exportability, deletion behavior, and downstream processors. Avoid collecting data that the product does not need.

## Related documentation

- [Backup and restore](../operations/backup-and-restore.md)
- [Retention and lifecycle](../operations/retention-and-lifecycle.md)
- [Web and input security](web-and-input-security.md)

