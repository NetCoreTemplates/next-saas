# Secrets

Production secrets belong in a platform secret manager or protected deployment environment, never in Git, browser bundles, logs, audit metadata, screenshots, or support notes.

[Operations](README.md) · [Configuration](configuration.md)

## Secret inventory

- `ConnectionStrings__DefaultConnection`
- `Stripe__SecretKey` or `STRIPE_SECRET_KEY`
- `Stripe__WebhookSecret`
- `SmtpConfig__UserName` and `SmtpConfig__Password`
- `SERVICESTACK_LICENSE` for commercial licenses only; the checked-in free OSS license must be registered in source code
- deployment registry, SSH, and cloud-storage credentials
- `APPSETTINGS_JSON` / `APPSETTINGS_JSON_BASE64` when using the included release workflow

Stripe's publishable key is designed for browser use, but it should still match the intended test/live environment. Raw API keys created by customers are user secrets and are shown only once.

## ASP.NET data-protection keys

`Program.cs` persists data-protection keys under `MyApp/App_Data`. Preserve and protect this key ring across deployments. Losing it can invalidate authentication cookies and protected Identity tokens; exposing it weakens protected application data.

In multi-instance production, use a shared protected key repository and configure an application name. A container-local key ring is not sufficient.

## Included GitHub/Kamal flow

The release workflow reads `APPSETTINGS_JSON` from GitHub Actions secrets, base64-encodes it for transport, and passes `APPSETTINGS_JSON_BASE64` through Kamal. `.kamal/secrets` maps the value into the container. Before ASP.NET Core constructs its host, `Program.cs` validates and flattens that JSON into the normal double-underscore configuration keys, so Hosting Startup modules see the selected database and policy immediately. The decoded document is not written into the image or container filesystem.

ServiceStack free and OSS keys cannot be supplied through the `SERVICESTACK_LICENSE` environment variable. This template registers its OSS key in `Configure.AppHost.cs`, including in tests and fork builds. Leave the GitHub secret absent for OSS deployments. A derived commercial application may set the secret to its commercial key, which takes precedence over the checked-in fallback.

Alternatively, pass individual environment variables from the target platform. Avoid maintaining the same secret in both a JSON bundle and separate variables unless precedence and rotation are explicit.

## Rotation procedure

1. inventory every consumer and environment;
2. create the replacement without deleting the active value;
3. deploy/configure the new value;
4. verify database, Stripe, email, storage, and login behavior;
5. revoke the previous credential;
6. monitor failures and document completion.

Database and provider rotations may need an overlap window. Stripe webhook endpoint secret rotation must remain synchronized with the endpoint configuration; invalid signatures are intentionally rejected.

For a suspected leak, revoke first when safe, inspect request/audit/provider logs by request ID and timestamp, rotate dependent credentials, and invalidate affected sessions or customer keys.

## CI safety

- Mask secrets and never echo decoded configuration.
- Pin permissions for deployment workflows.
- Do not run untrusted pull-request code with production secrets.
- Keep test and live Stripe credentials in separate protected environments.
- Scan commits and built frontend assets before release.

## Related documentation

- [Deployment](deployment.md)
- [External services](external-services.md)
- [Backup and restore](backup-and-restore.md)
