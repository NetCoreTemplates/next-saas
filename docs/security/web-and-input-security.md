# Web and input security

Treat every browser value, route identifier, file, API header, webhook, and generated link as untrusted until the server validates it in context.

[Security](README.md) · [Add a ServiceStack API](../development/add-a-servicestack-api.md)

## Request validation

ServiceStack DTOs use declarative validation for required values, email, ranges, and positive units. Services add domain validation, role/capability checks, tenant predicates, current subscription state, idempotency, and database constraints.

Use allow-lists for enum-like values, currency, file extensions, callback destinations, and provider event types. Keep error codes stable and avoid revealing whether a cross-tenant resource exists.

Parameterize all database access through OrmLite or provider APIs. Never build SQL, filesystem paths, HTML, shell commands, or provider URLs by concatenating untrusted values.

## Browser security

React escapes rendered strings by default; avoid `dangerouslySetInnerHTML` for customer content. Razor encodes output by default. When rendering maintained Markdown, keep the parser/configuration free of arbitrary scripts and review any future raw-HTML support.

ASP.NET Core Razor forms use antiforgery conventions. Cookie-authenticated mutations under `/saas` additionally require a same-origin `Origin` or `Referer`; missing or foreign origins return `CsrfValidationFailed`. API-key requests are exempt from the browser check and authenticate independently. Configure `Security.RequireSameOriginForCookieApi`, and verify cookie `Secure`, `HttpOnly`, `SameSite`, lifetime, and sign-out behavior in the deployed environment.

The host enables HTTPS redirection and HSTS in Production. `Configure.Security.cs` also applies CSP, frame denial, `nosniff`, strict-origin referrer policy, restricted camera/microphone/geolocation permissions, and cross-origin opener isolation. The compatible default CSP permits inline scripts/styles for the static Next.js and existing UI output; tighten it with hashes/nonces when the derived application can support them. Override `Security.ContentSecurityPolicy` when adding a deliberate external origin.

Restrict `AllowedHosts` and CORS origins. Do not use wildcard credentialed CORS. The static Next.js export does not need a separate public Node server in Production.

## Upload and download safety

- Enforce request/body and configured file-size limits while streaming.
- Generate opaque object keys; never join user filenames to storage paths.
- Validate content when the product will parse or preview it.
- Scan untrusted files and quarantine pending results.
- Serve downloads with safe content disposition and type handling.
- Run document/PDF/media processors with constrained resources and no implicit network/credential access.

## Abuse and availability

Apply layered limits at CDN/proxy, authentication, API credential, organization quota, queue admission, database, and provider boundaries. Set timeouts and maximum result/page sizes. Avoid unbounded exports, searches, fan-out, or background jobs.

## Related documentation

- [API credentials and abuse controls](api-credentials-and-abuse-controls.md)
- [File storage](../features/file-storage.md)
- [Production security review](production-security-review.md)
