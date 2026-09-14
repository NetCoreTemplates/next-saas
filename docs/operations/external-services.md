# External services

Stripe and SMTP are the two production dependencies included by default. The application remains usable on Free plans without Stripe in development, but the default production policy requires both.

[Operations](README.md) · [Stripe sandbox setup](../getting-started/06-connect-stripe-sandbox.md)

## Stripe

Required production values are publishable key, secret/restricted key, and webhook signing secret. Configure the webhook endpoint as:

```text
POST https://your-host/stripe/webhook
```

Provision Products and recurring Prices from the plan draft UI only when catalog provisioning policy allows it. Automatic live provisioning is disabled by default. Verify currency, interval, amount, trial policy, and metadata before publishing a plan.

Operational checks:

- Checkout returns to `/billing` and confirmation updates the local projection.
- Signed webhook deliveries create/process unique inbox rows.
- Customer Portal returns to the correct public base URL.
- Hourly reconciliation repairs missed later transitions.
- Stripe mode matches every Product, Price, Customer, and key.

Stripe remains authoritative for payments, invoices, discounts, taxes, and subscription objects. The local projection remains authoritative for fast access decisions. Never query Stripe in normal authorization paths.

When an organization appears on the wrong plan, inspect Checkout Session metadata, local `BillingSubscription`, the Stripe inbox, webhook delivery history, and reconciliation—do not edit the plan ID directly.

## SMTP and email

Set `Notifications.Provider=Smtp` plus `SmtpConfig` host, port, credentials, and sender. Use a monitored `Product.SupportEmail` on a verified domain.

Verify:

- account confirmation and password reset;
- organization invitations;
- quota/billing notifications;
- SPF, DKIM, DMARC, bounce, and complaint handling;
- provider throttling and retry behavior.

`Development` provider records messages locally; it does not test actual deliverability. `Disabled` prevents email invitations and account recovery from reaching users.

## Provider outages

Keep customer requests bounded, persist a recoverable inbox/delivery record, expose failures to operators, and retry with stable idempotency. Do not bypass signature validation or quota/billing policy during an outage.

## Related documentation

- [Billing and subscriptions](../features/billing-and-subscriptions.md)
- [Notifications](../features/notifications.md)
- [Background jobs and recovery](background-jobs-and-recovery.md)

