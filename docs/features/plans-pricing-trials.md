# Plans, pricing, and trials

Plans are versioned commercial contracts containing prices, features, quotas, and optional trial duration.

[Documentation home](../README.md) · [Plans and entitlements concept](../concepts/plans-and-entitlements.md)

## Administration workflow

Only platform Admin users manage the catalog from the **Plans** tab at `/admin/plans`:

1. Select a durable `SaasPlan`.
2. Edit or create its draft `SaasPlanVersion`.
3. Configure public visibility, description, order, contact-sales behavior, and trial days.
4. Add monthly/annual `SaasPlanPrice` rows, feature grants, and quota allowances.
5. Optionally create missing Products and Prices in Stripe.
6. Save, review active-subscription impact, and publish.

Publishing retires the prior published version. Existing subscriptions remain pinned to their version; new selections use the new version.

![Plan Editor and Version Management](../assets/plan-editor.png)

## Models

- `SaasPlan` — stable identity, code, and catalog-level display data.
- `SaasPlanVersion` — draft, published, or retired immutable contract.
- `SaasPlanPrice` — currency, monthly/yearly interval, minor-unit amount, Stripe Price mapping, and active status.
- `SaasPlanFeature` — registered feature grant.
- `SaasPlanQuota` — registered meter allowance, enforcement, and rollover setting.

![Immutable Plan Versions and Historical Pinning](../assets/plan-versions.png)

`MyApp/plans.json` seeds these records only for an empty database.

## APIs

- `GET /saas/plans` — public published plan projection.
- `GET /saas/admin/plans/{PlanId}` — current draft or published details.
- `POST /saas/admin/plans/{PlanId}` — save a draft.
- `POST /saas/admin/plans/{PlanId}/publish` — publish the draft.
- `POST /saas/admin/plans/{PlanId}/stripe-catalog` — create/reuse Stripe objects.

## Pricing rules

Amounts are integer minor currency units. Supported currencies are defined globally by `Saas.SupportedCurrencies`; annual billing is globally controlled by `Saas.EnableAnnualBilling`.

A self-service paid Price must be active, positive, published, and mapped to a Stripe `price_...` identifier. Free plans require no Stripe Price. Contact-sales plans intentionally bypass self-service Checkout.

![Multi-Currency and Interval Plan Prices](../assets/plan-prices.png)

Stripe catalog provisioning uses plan metadata and idempotency keys. It fills draft mappings but never publishes a plan automatically. Live provisioning requires explicit opt-in.

## Trials

Trial duration belongs to a plan version. Global policy controls whether it is honored:

- `Saas.EnableTrials` enables plan trials;
- `Saas.DefaultTrialDays` supplies an administrative default;
- `Saas.TrialRequiresPaymentMethod` controls upfront payment collection.

The admin editor accepts 1–365 days. Stripe determines hosted Checkout wording and trial payment behavior. A trial without a payment method is configured to cancel safely if no payment method exists at trial end.

![Trial Duration and Payment Policy](../assets/trial-settings.png)

## Authorization and audit

Catalog APIs require platform Admin. Draft saves, publication, and Stripe catalog provisioning produce audit events. Customer plan choice continues through organization billing authorization.

## Extension points

- Add feature and meter definitions in JSON before granting them in plans.
- Extend plan display through `PlanInfo` rather than returning persistence rows to public pages.
- Implement deliberate subscription migration if customers should adopt a new version automatically.
- Add currencies only after checking zero/three-decimal currency behavior in both UI and Stripe.

## Failure modes

- Unknown feature/meter key: reject draft.
- Missing Stripe mapping: plan can publish but paid checkout is not ready.
- Live catalog provisioning disabled: create in Stripe manually and paste mappings.
- Published plan edited: use a new draft rather than mutation.
- Seed file changed on populated DB: no effect until an explicit migration or reset.

## Verify

Create and publish a draft, confirm existing subscriptions retain the old version, verify public pricing, provision sandbox objects twice without duplicates, and complete monthly, annual, trial, and contact-sales paths.

## Related documentation

- [Billing and subscriptions](billing-and-subscriptions.md)
- [Coupons](coupons.md)
- [Usage and quotas](../concepts/usage-and-quotas.md)
