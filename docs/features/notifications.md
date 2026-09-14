# Notifications

The notification subsystem provides in-app and email delivery, customer preferences, deduplication, retries, and an operator failure queue.

[Documentation home](../README.md) · [Background processing](../concepts/background-processing.md)

## APIs

- `GET /saas/notifications/preferences`
- `POST /saas/notifications/preferences`
- `GET /saas/notifications?unreadOnly=true`
- `POST /saas/notifications/{Id}/read`
- `POST /saas/admin/notifications/{Id}/retry`

Preferences are stored per user, organization, template key, and channel. A template-specific preference wins over the `*` fallback.

## Delivery flow

Call `INotificationManager.Queue` with recipient, template key, rendered subject/body, and a stable deduplication key. The manager creates at most one delivery per key and channel.

In-app delivery is stored immediately when enabled. SMTP delivery is processed by `ProcessNotificationDeliveryCommand` and the ServiceStack email background job. Attempts, provider identifier, delivery state, and the last error remain visible to operators.

Development mode records email as delivered to the local development inbox without contacting a provider. It does not prove SMTP or production deliverability.

## Configuration

```json
{
  "Notifications": {
    "FromName": "Acme",
    "FromEmail": "noreply@example.com",
    "Provider": "Development",
    "EnableInApp": true,
    "MaxAttempts": 5
  }
}
```

Supported providers are `Development`, `Smtp`, and `Disabled`. `Smtp` also requires `SmtpConfig`. Production preflight rejects missing required email configuration.

## Design rules

- Persist the delivery before enqueueing work.
- Use a deterministic business-event key, not a random retry key.
- Keep secrets and unnecessary personal data out of delivery metadata.
- Honor preferences for optional messages; define transactional exceptions deliberately.
- Treat provider acceptance and inbox delivery as different outcomes.

## Retention and recovery

`Saas.NotificationRetentionDays` supplies global retention and can be overridden by an organization retention policy. Failed deliveries appear at `/admin/operations` for authorized retry. Retries reuse the existing delivery record and stop at `Notifications.MaxAttempts`.

## Verify

Test wildcard and template-specific preferences for both channels, duplicate queue calls, SMTP success/failure, exhausted attempts, operator retry, in-app read state, tenant isolation, and retention cleanup.

## Related documentation

- [Organizations and members](organizations-and-members.md)
- [Audit logs](audit-logs.md)
- [Support operations](support-operations.md)
