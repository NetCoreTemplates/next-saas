# Plans and entitlements

Plans describe commercial packages. Entitlements answer whether an organization may use a capability. Published plan versions make those answers stable for existing subscriptions while the catalog evolves.

[Documentation home](../README.md) · [Architecture](architecture.md)

## Model

```text
SaasPlan
  └── SaasPlanVersion
        ├── SaasPlanPrice
        ├── SaasPlanFeature
        └── SaasPlanQuota

BillingSubscription ──pins──> SaasPlanVersion
CustomerEntitlementOverride ──overrides──> feature or meter key
```

`SaasPlan` is the durable product identity and public code. A version is an immutable commercial contract after publication. Prices, features, quotas, and trial days belong to that version.

## Plan lifecycle

The admin workflow is draft then publish:

1. An Admin edits a draft version.
2. Stripe Products and Prices can be provisioned for missing draft mappings.
3. Validation rejects unknown feature/meter keys and invalid price or trial values.
4. Publishing retires the previous published version and makes the draft available to new selections.
5. Existing `BillingSubscription` rows remain pinned to their prior version.

Changing the public name on `SaasPlan` or editing a draft must not silently rewrite an existing customer’s allowances or price contract.

![Plan Editor and Catalog Management](../assets/plan-editor.png)

## Feature registry

Feature keys are defined under `Saas.Features` in JSON. A definition supplies display metadata and a global fallback:

```json
{
  "Key": "analytics.advanced",
  "DisplayName": "Advanced analytics",
  "Description": "View usage breakdowns and projections.",
  "Category": "Analytics",
  "DefaultEnabled": false
}
```

Plan administration may grant only registered keys. This prevents a typo from becoming a silent, permanently stored contract.

Use domain-stable names such as `reports.export` or `api.access`. Do not use Stripe Product/Price IDs or UI labels as feature keys.

## Resolution precedence

For a feature, `IEntitlementResolver` applies:

1. an active customer override;
2. the subscription’s pinned plan version, or the published Free version during `FreeFallback`;
3. the configured global fallback.

An override is active when its optional start has arrived and its optional expiry has not. Expired overrides are ignored without requiring destructive cleanup.

![Entitlement Resolution Precedence](../assets/entitlement-precedence.png)

`GetEffectiveEntitlements` returns each effective value, source, and expiry. The dashboard includes the same information for explanatory UI.

## Server and client enforcement

Use `[RequiresFeature("feature.key")]` on the ServiceStack request DTO for normal feature enforcement. The global request filter resolves the current organization, subscription, and effective entitlement.

```csharp
[ValidateIsAuthenticated]
[RequiresFeature("reports.export")]
[Route("/reports/export", "POST")]
public class ExportReport : IPost, IReturn<ExportReportResponse> { }
```

React may use `FeatureGate` or dashboard entitlement state to hide controls and explain upgrades. The API remains authoritative because clients can be modified or bypassed.

## Quotas are related but distinct

A feature answers yes/no. A plan quota answers how much. A feature may be enabled without a quota, and a quota may vary by plan or customer contract.

Customer overrides reuse `CustomerEntitlementOverride`:

- `Enabled` overrides a feature;
- `QuotaUnits` overrides a meter allowance;
- `ValidFrom` and `ValidUntil` bound its applicability;
- `Reason` and audit metadata explain why it exists.

Avoid encoding quotas inside feature keys such as `reports.100`. Keep capability and allowance independently queryable.

## Free fallback and access modes

`BillingSubscription.AccessMode` controls broad workspace access. When global past-due policy chooses `FreeFallback`, feature and quota resolution uses the published Free plan while retaining the customer’s pinned paid version.

Other modes may permit full, grace, read-only, or suspended behavior. Access-mode evaluation happens before product writes. A subscription transition does not delete product data or alter plan history.

## Initial plans versus runtime plans

`MyApp/plans.json` seeds an empty database. It is ideal for template customization and deterministic tests. After deployment, plan state is owned by the RDBMS and managed from the **Plans** tab at `/admin/plans`.

Changing `plans.json` does not update a populated database. Use a plan draft/publish workflow or a deliberate migration for an existing installation.

## Customer exceptions

Overrides are meant for negotiated contracts, temporary enablement, incident recovery, or controlled rollout—not as a replacement for coherent plans.

Every override should:

- use a registered key;
- record a business reason and actor;
- have an expiry when the exception is temporary;
- appear in Customer 360 diagnostics;
- produce an audit event on creation and removal.

## Adding a feature safely

1. Define its stable key in `Saas.Features`.
2. Add it to relevant clean-state plans and runtime drafts.
3. Protect server APIs with `RequiresFeature` or explicit resolver policy.
4. Add explanatory UI using effective entitlement state.
5. Test plan grant, fallback, override, override expiry, and denial.
6. Update `features.json`.

## Common mistakes

- Editing a published plan version in place.
- Assuming publishing migrates existing subscriptions.
- Checking only the browser entitlement.
- Using price IDs as product capabilities.
- Creating an unregistered override key.
- Using indefinite overrides for ordinary plan design.
- Treating a payment failure as permission to delete customer data.

## Related concepts

- [Architecture](architecture.md)
- [Organizations and tenancy](organizations-and-tenancy.md)
- [Usage and quotas](usage-and-quotas.md)
- [Add a metered feature](../getting-started/05-add-a-metered-feature.md)
