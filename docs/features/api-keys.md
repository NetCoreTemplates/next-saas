# API keys

API keys provide organization-bound programmatic access without exposing the full ServiceStack scope editor in the customer UI.

[Documentation home](../README.md) · [Organizations and tenancy](../concepts/organizations-and-tenancy.md)

## Customer workflow

An authenticated member selects an organization, opens **Settings → API keys**, and creates a named credential. The raw key is displayed once. Later views show only its fingerprint and metadata, so a lost key must be replaced rather than recovered.

The management page intentionally asks only for a name and optional expiry. The server assigns the template's standard scopes: `usage:read`, `usage:write`, and `workspace:read`.

## ServiceStack integration

`Configure.ApiKeys.cs` registers `ApiKeysFeature` and applies SaaS-specific request and response filters around ServiceStack's API-key DTOs.

The filters:

- bind a new key to the active organization through `RefIdStr`;
- require the `api.access` entitlement;
- filter lists to the active organization;
- prevent moving a key between organizations;
- require an interactive session to create, update, or delete credentials;
- prevent an API key from managing other keys;
- record create, update, and delete audit events.

Use the generated ServiceStack DTOs for the exact API-key routes and payloads. The account UI lives in `MyApp/Areas/Identity/Pages/Account/Manage/ApiKeys.cshtml`.

## Authentication and isolation

Send a credential using the ServiceStack bearer or API-key authentication supported by `ApiKeysFeature`. The organization is resolved from the stored key, not from the browser's active selection or a caller-supplied organization ID.

Revoked, expired, or cross-organization credentials fail before product data is returned. Credential material must never be logged or added to audit metadata.

## Rate limiting

`Saas.ApiKeyRequestsPerMinute` sets a per-process, fixed-window limit keyed by organization and a SHA-256 hash of the credential. Rejection returns HTTP `429` and `Retry-After`.

The included limiter is suitable for a single application instance. Replace `SaasApiRateLimiter` with a shared Redis or gateway-backed implementation before horizontally scaling.

## Extension points

- Change the fixed scope set in `Configure.ApiKeys.cs` if the product exposes different programmatic operations.
- Add credential-level usage attribution by storing a non-secret key identifier on usage events.
- Add rotation overlap or IP restrictions only when customers need them; keep the default UI small.
- Preserve organization binding and interactive management rules when replacing `ApiKeysFeature` UI.

## Verify

Create a key in organization A, copy it once, call an allowed API, and confirm it appears in that organization's list. Switch to organization B and confirm it is absent. Test expiry, revocation, the rate limit, missing `api.access`, and an API-key attempt to create another key.

## Related documentation

- [Organizations and members](organizations-and-members.md)
- [Usage analytics](usage-analytics.md)
- [Audit logs](audit-logs.md)

