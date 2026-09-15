# Stripe webhook security

The Stripe webhook is unauthenticated by user session and therefore authenticates the provider message cryptographically before trusting its content.

[Security](README.md) · [Billing and subscriptions](../features/billing-and-subscriptions.md)

## Validation flow

`POST /stripe/webhook`:

1. reads the original raw request body;
2. requires `Stripe__WebhookSecret`;
3. validates `Stripe-Signature` using the Stripe .NET SDK;
4. rejects invalid signatures with `InvalidStripeSignature` before inserting trusted state;
5. inserts a unique `StripeEventInbox` row by Stripe event ID;
6. acknowledges duplicates without reapplying them;
7. enqueues `ProcessStripeEventCommand` for replay-safe application.

Request-body logging explicitly excludes the webhook DTO. Keep the endpoint behind HTTPS and do not place a generic proxy/body transformation in front of signature verification.

![Stripe Webhook Ingestion and Verification Flow](../assets/webhook-flow.png)

## Tenant correlation

Checkout attaches opaque organization and plan-version metadata. Webhook application resolves the organization from controlled metadata or an existing Stripe Customer mapping. It never trusts a browser-provided subscription status.

Validate that Product, Price, Customer, Subscription, and keys all belong to the same Stripe account and mode. Treat unknown/missing organization mappings as recoverable operational events, not a reason to attach the event to a guessed tenant.

## Replay and ordering

Stripe may retry, duplicate, or deliver events out of order. Handlers update the local projection idempotently and reconciliation compares known Stripe subscriptions later. Never produce a second entitlement grant, notification, or usage mutation merely because the same event ran again.

The browser Checkout-success confirmation covers the immediate return path; it does not replace signed webhooks for future renewals, failures, cancellation, and portal changes.

## Secret rotation and incident handling

Coordinate webhook endpoint secret rotation with the deployed `Stripe__WebhookSecret`. Test a signed event after rotation before retiring the previous endpoint/configuration. Do not temporarily disable validation to restore billing.

For suspicious events, preserve the inbox/event ID, request ID, timestamp, signature outcome, and Stripe Dashboard delivery. Reconcile from Stripe's authenticated API only after resolving the cause.

## Verify

Test valid signature, tampered body, absent/wrong secret, duplicate ID, retry, out-of-order relevant events, missing tenant mapping, test/live mismatch, failed command recovery, and reconciliation.

## Related documentation

- [External services](../operations/external-services.md)
- [Background jobs and recovery](../operations/background-jobs-and-recovery.md)
- [Audit logs](../features/audit-logs.md)

