# API credentials and abuse controls

ServiceStack `ApiKeysFeature` supplies credentials; SaaS filters bind their lifecycle and access to the active organization.

[Security](README.md) · [API keys](../features/api-keys.md)

## Credential lifecycle

An interactive authenticated member with `api.access` creates a named key for the active organization. The management UI shows the raw key once and later exposes only a visible fingerprint/metadata. Updates and deletes must match both the creating user and active organization.

API-key-authenticated requests cannot create, update, or delete credentials and cannot switch/list the interactive user's active organization. Revoked/expired keys and keys whose owner lost organization membership are rejected.

Accepted headers are:

```text
X-Api-Key: ak-...
Authorization: Bearer ak-...
```

The configured standard scopes are `usage:read`, `usage:write`, and `workspace:read`. The simplified UI intentionally does not let customers compose arbitrary scopes.

![API Keys Creation and Standard Scopes](../assets/api-keys-manager.png)

![API Key Fingerprint Display](../assets/api-key-fingerprint.png)

## Storage sensitivity

The current ServiceStack key verifier looks up the supplied value in the `ApiKey` table. Treat that table, database credentials, backups, query tooling, and administrator access as credential-bearing sensitive material. A derived product requiring non-recoverable at-rest keys should implement a prefix/identifier plus strong one-way verifier design and a migration/rotation plan.

Never write raw keys to application logs, audit detail, support notes, analytics dimensions, URLs, or client error reporting.

![Programmatic API Request with Bearer Token](../assets/api-request.png)

## Abuse controls

`Saas.ApiKeyRequestsPerMinute` applies a fixed one-minute in-process limit by organization plus SHA-256 credential hash. Rejection returns `429` and `Retry-After`. Commercial quota enforcement is separate and returns `QuotaExceeded` when a metered allowance is exhausted.

Before scaling horizontally, replace `SaasApiRateLimiter` with a distributed limiter or enforce limits at a trusted gateway. Add independent limits for IP/organization/endpoint cost, body size, concurrent expensive work, authentication endpoints, and provider-backed operations.

Do not allow many cheap requests to enqueue unlimited expensive jobs. Admission control should happen before durable queue growth.

## Rotation and response

Create a replacement, deploy it to the caller, verify traffic, then revoke the old key. Because a raw key cannot be shown again in the UI, loss means replacement.

For compromise, revoke immediately, find usage/audit/request activity by visible fingerprint, actor, tenant, request ID, and time, inspect affected resources, and notify the customer according to incident policy.

## Verify

Test one-time display, organization filtering, inactive membership, expiry, revocation, management via API key, both header forms, fixed scopes, rate-limit isolation/reset, quota interaction, logs/audit redaction, and multi-instance behavior.

## Related documentation

- [Tenant isolation](tenant-isolation.md)
- [Usage and quotas](../concepts/usage-and-quotas.md)
- [Secrets](../operations/secrets.md)

