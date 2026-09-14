# Billing and subscriptions

Stripe owns financial state; Next SaaS maintains a local subscription projection for fast, deterministic access decisions.

[Documentation home](../README.md) · [Stripe sandbox setup](../getting-started/06-connect-stripe-sandbox.md)

## User behavior

Customers choose a published Price from `/pricing` or `/billing`. `CreateCheckoutSession` returns a Stripe-hosted Checkout URL. After completion, the browser returns to `/billing`, which confirms the Checkout Session, while signed webhooks continue to handle future renewals, failures, portal changes, and cancellation.

The Stripe Customer Portal is the default UI for payment methods, invoices, cancellation, and subscription changes. The template links to Stripe-hosted invoice PDFs rather than generating duplicate tax documents.

## Models and APIs

| Area | Types and routes |
| --- | --- |
| Local projection | `BillingSubscription`, `WorkspaceAccessMode` |
| Start checkout | `POST /saas/billing/checkout` |
| Confirm return | `POST /saas/billing/checkout/confirm` |
| Customer Portal | `POST /saas/billing/portal` |
| Stripe events | `POST /stripe/webhook`, `StripeEventInbox` |
| Operator repair | reconciliation and Stripe retry APIs under `/saas/admin` |

Checkout accepts a local `SaasPlanPrice.Id`, not an arbitrary Stripe Price ID. The server loads the published active price and attaches opaque organization and plan-version metadata.

## Access projection

`BillingSubscription` pins a plan version and stores Stripe identifiers, status, billing period, trial dates, cancellation data, and local access mode.

Access modes are:

- `Full` — normal paid or trial access;
- `Grace` — temporary access during the configured past-due grace window;
- `ReadOnly` — product reads remain available but writes are blocked;
- `FreeFallback` — resolve features and quotas from the published Free plan;
- `Suspended` — block product access.

`Saas.PastDueGraceDays` and `Saas.AfterGraceAccessMode` define global policy. Subscription transitions never delete customer data.

## Webhook processing

The webhook endpoint validates the raw body with `Stripe.WebhookSecret`, inserts each Stripe event once into `StripeEventInbox`, and enqueues `ProcessStripeEventCommand`. Duplicate delivery is safe. Processing updates the local projection and audit trail.

Failed inbox rows retain attempts and error details. BillingAdmin or Admin operators can inspect and retry them from `/admin/operations`.

## Configuration

```json
{
  "Stripe": {
    "PublishableKey": "",
    "SecretKey": "",
    "WebhookSecret": "",
    "PortalConfigurationId": "",
    "EnableCatalogProvisioning": true,
    "AllowLiveCatalogProvisioning": false
  }
}
```

Supply secrets through environment configuration. Free plans work without Stripe; paid calls return a stable configuration error. Production policy requires Stripe by default but can be explicitly relaxed for a free-only product.

## Authorization

Organization Owner, Admin, and Billing members can manage billing. Member access is denied server-side. Platform BillingAdmin can diagnose and reconcile billing but is not automatically an organization member.

## Failure and recovery

- Browser success before webhook: Checkout confirmation asks Stripe for the completed Session and updates local state.
- Missed later webhook: hourly reconciliation repairs subscriptions with known Stripe IDs.
- Invalid signature: reject without inserting a trusted event.
- Duplicate event: return safely without repeating transitions.
- Provider outage: retain failed inbox state for operator retry.
- Missing Price mapping: block checkout with a descriptive catalog error.

## Extension points

Use `IStripeBillingGateway` for Stripe interactions. Keep product authorization dependent on the local projection. Add provider-specific operations behind the gateway, use stable idempotency keys, and preserve webhook inbox semantics.

## Verify

Test paid checkout, trials, discounts, payment failure/recovery, portal changes, cancellation, event replay, invalid signatures, and reconciliation. Confirm local plan/access/period state after every scenario.

## Related documentation

- [Plans, pricing, and trials](plans-pricing-trials.md)
- [Coupons](coupons.md)
- [Plans and entitlements](../concepts/plans-and-entitlements.md)
- [Background processing](../concepts/background-processing.md)
