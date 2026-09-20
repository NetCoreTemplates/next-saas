import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  BillingInterval,
  PlanAudience,
  PlanVersionStatus,
  ProvisionSaasPlanStripeCatalog,
  ProvisionSaasPlanStripeCatalogResponse,
  SaasPlan,
  SaasPlanDetails,
  SaasPlanPrice,
  SaasPlanVersion,
  SaveSaasPlanDraft,
} from '@/lib/dtos'

const mocks = vi.hoisted(() => ({
  api: vi.fn(),
}))

vi.mock('@/lib/gateway', () => ({
  client: { api: mocks.api },
}))

vi.mock('@/components/app-shell', () => ({
  Panel: ({ children, className }: React.PropsWithChildren<{ className?: string }>) => <div className={className}>{children}</div>,
  StatusPill: ({ children }: React.PropsWithChildren) => <span>{children}</span>,
}))

import { PlanEditor } from './plan-editor'

describe('PlanEditor', () => {
  beforeEach(() => {
    mocks.api.mockReset()
    vi.restoreAllMocks()
    mocks.api.mockResolvedValue({
      succeeded: true,
      response: new SaasPlanDetails({
        plan: new SaasPlan({ id: 'plan.free', code: 'free', name: 'Free' }),
        version: new SaasPlanVersion({ id: 'plan.free.v1', planId: 'plan.free', version: 1, status: PlanVersionStatus.Published }),
        prices: [],
        features: [],
        quotas: [],
      }),
    })
  })

  it('loads the initially selected plan once in React Strict Mode', async () => {
    const plan = new SaasPlan({ id: 'plan.free', code: 'free', name: 'Free' })
    const version = new SaasPlanVersion({ id: 'plan.free.v1', planId: plan.id, version: 1, status: PlanVersionStatus.Published })

    render(<StrictMode><PlanEditor plans={[plan]} versions={[version]} trialsEnabled trialRequiresPaymentMethod={false} defaultTrialDays={14} stripeConfigured={false} stripeCatalogProvisioningEnabled={false} stripeMode="Not configured" onPlanUpdated={() => {}}/></StrictMode>)

    await waitFor(() => expect(screen.queryByText('Loading plan configuration')).toBeNull())
    expect(screen.getByRole('heading', { name: 'Free' })).toBeTruthy()
    expect(mocks.api).toHaveBeenCalledTimes(1)
  })

  it('explains that Stripe must be configured before catalog provisioning', async () => {
    const plan = new SaasPlan({ id: 'plan.pro', code: 'pro', name: 'Pro' })
    const version = new SaasPlanVersion({ id: 'plan.pro.v1', planId: plan.id, version: 1, status: PlanVersionStatus.Published })
    mocks.api.mockResolvedValueOnce({
      succeeded: true,
      response: new SaasPlanDetails({ plan, version, prices: [], features: [], quotas: [] }),
    })

    render(<PlanEditor plans={[plan]} versions={[version]} trialsEnabled trialRequiresPaymentMethod={false} defaultTrialDays={14} stripeConfigured={false} stripeCatalogProvisioningEnabled={false} stripeMode="Not configured" onPlanUpdated={() => {}}/>)
    await waitFor(() => expect(screen.queryByText('Loading plan configuration')).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: /Pricing/ }))

    const provision = screen.getByRole('button', { name: 'Create missing in Stripe' })
    expect((provision as HTMLButtonElement).disabled).toBe(true)
    expect(provision.getAttribute('title')).toBe('Set Stripe__SecretKey and restart the application')
  })

  it('creates a draft automatically when provisioning a published plan with no edits', async () => {
    const plan = new SaasPlan({ id: 'plan.pro', code: 'pro', name: 'Pro', description: 'For teams', audience: PlanAudience.Business })
    const published = new SaasPlanVersion({ id: 'plan.pro.v1', planId: plan.id, version: 1, status: PlanVersionStatus.Published, audience: PlanAudience.Business })
    const draftVersion = new SaasPlanVersion({ id: 'plan.pro.v2', planId: plan.id, version: 2, status: PlanVersionStatus.Draft, audience: PlanAudience.Business })
    const unmapped = new SaasPlanPrice({ id: 'price.pro.v1.month', planVersionId: published.id, currency: 'usd', interval: BillingInterval.Month, unitAmount: 4900, isActive: true })
    const mapped = new SaasPlanPrice({ ...unmapped, planVersionId: draftVersion.id, stripePriceId: 'price_stripe_pro_month' })
    const initial = new SaasPlanDetails({ plan, version: published, hasDraft: false, prices: [unmapped], features: [], quotas: [] })
    const saved = new SaasPlanDetails({ plan, version: draftVersion, hasDraft: true, prices: [unmapped], features: [], quotas: [] })
    const provisioned = new SaasPlanDetails({ plan, version: draftVersion, hasDraft: true, prices: [mapped], features: [], quotas: [] })
    mocks.api.mockImplementation(request => Promise.resolve(request instanceof SaveSaasPlanDraft
      ? { succeeded: true, response: saved }
      : request instanceof ProvisionSaasPlanStripeCatalog
        ? { succeeded: true, response: new ProvisionSaasPlanStripeCatalogResponse({ stripeProductId: 'prod_pro', prices: [{ currency: 'usd', interval: BillingInterval.Month, unitAmount: 4900, stripePriceId: 'price_stripe_pro_month', created: true }], draft: provisioned }) }
        : { succeeded: true, response: initial }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<PlanEditor plans={[plan]} versions={[published]} trialsEnabled trialRequiresPaymentMethod={false} defaultTrialDays={14} stripeConfigured stripeCatalogProvisioningEnabled stripeMode="Sandbox" onPlanUpdated={() => {}}/>)
    await waitFor(() => expect(screen.queryByText('Loading plan configuration')).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: /Pricing/ }))
    expect((screen.getByRole('button', { name: 'Save draft' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Create missing in Stripe' }))

    await waitFor(() => expect(screen.getByText(/The mappings were saved to the draft; publish it to enable Checkout/)).toBeTruthy())
    expect(mocks.api.mock.calls.map(([request]) => request.constructor.name)).toEqual([
      'GetSaasPlanDetails', 'SaveSaasPlanDraft', 'ProvisionSaasPlanStripeCatalog',
    ])
    const draftRequest = mocks.api.mock.calls[1][0] as SaveSaasPlanDraft
    expect(draftRequest.audience).toBe(PlanAudience.Business)
    expect(draftRequest.prices?.[0].unitAmount).toBe(4900)
    expect((screen.getByRole('button', { name: 'Publish changes' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('saves unsaved price edits before provisioning Stripe', async () => {
    const plan = new SaasPlan({ id: 'plan.pro', code: 'pro', name: 'Pro', description: 'For teams' })
    const version = new SaasPlanVersion({ id: 'plan.pro.v2', planId: plan.id, version: 2, status: PlanVersionStatus.Draft })
    const current = new SaasPlanPrice({ planVersionId: version.id, currency: 'usd', interval: BillingInterval.Month, unitAmount: 4900, isActive: true })
    const updated = new SaasPlanPrice({ ...current, unitAmount: 5900 })
    const mapped = new SaasPlanPrice({ ...updated, stripePriceId: 'price_stripe_pro_month' })
    const initial = new SaasPlanDetails({ plan, version, hasDraft: true, prices: [current], features: [], quotas: [] })
    const saved = new SaasPlanDetails({ plan, version, hasDraft: true, prices: [updated], features: [], quotas: [] })
    const provisioned = new SaasPlanDetails({ plan, version, hasDraft: true, prices: [mapped], features: [], quotas: [] })
    mocks.api.mockImplementation(request => Promise.resolve(request instanceof SaveSaasPlanDraft
      ? { succeeded: true, response: saved }
      : request instanceof ProvisionSaasPlanStripeCatalog
        ? { succeeded: true, response: new ProvisionSaasPlanStripeCatalogResponse({ stripeProductId: 'prod_pro', prices: [{ currency: 'usd', interval: BillingInterval.Month, unitAmount: 5900, stripePriceId: 'price_stripe_pro_month', created: true }], draft: provisioned }) }
        : { succeeded: true, response: initial }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<PlanEditor plans={[plan]} versions={[version]} trialsEnabled trialRequiresPaymentMethod={false} defaultTrialDays={14} stripeConfigured stripeCatalogProvisioningEnabled stripeMode="Sandbox" onPlanUpdated={() => {}}/>)
    await waitFor(() => expect(screen.queryByText('Loading plan configuration')).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: /Pricing/ }))
    fireEvent.change(screen.getByLabelText('Minor units'), { target: { value: '5900' } })
    const button = screen.getByRole('button', { name: 'Create missing in Stripe' }) as HTMLButtonElement
    expect(button.disabled).toBe(false)
    fireEvent.click(button)

    await waitFor(() => expect(screen.getByText(/The mappings were saved to the draft; publish it to enable Checkout/)).toBeTruthy())
    expect((mocks.api.mock.calls[1][0] as SaveSaasPlanDraft).prices?.[0].unitAmount).toBe(5900)
    expect(mocks.api.mock.calls[2][0]).toBeInstanceOf(ProvisionSaasPlanStripeCatalog)
  })

  it('saves provisioned Stripe mappings into the draft and enables publishing immediately', async () => {
    const plan = new SaasPlan({ id: 'plan.pro', code: 'pro', name: 'Pro', description: 'Pro plan' })
    const version = new SaasPlanVersion({ id: 'plan.pro.v2', planId: plan.id, version: 2, status: PlanVersionStatus.Draft })
    const unmapped = new SaasPlanPrice({
      id: 'price.pro.v2.month', planVersionId: version.id, currency: 'usd',
      interval: BillingInterval.Month, unitAmount: 4900, isActive: true,
    })
    const mapped = new SaasPlanPrice({ ...unmapped, stripePriceId: 'price_stripe_pro_month' })
    const initial = new SaasPlanDetails({ plan, version, hasDraft: true, prices: [unmapped], features: [], quotas: [] })
    const saved = new SaasPlanDetails({ plan, version, hasDraft: true, prices: [mapped], features: [], quotas: [] })
    mocks.api.mockImplementation(request => Promise.resolve(request instanceof ProvisionSaasPlanStripeCatalog
      ? {
          succeeded: true,
          response: new ProvisionSaasPlanStripeCatalogResponse({
            stripeProductId: 'prod_pro', productCreated: true,
            prices: [{ currency: 'usd', interval: BillingInterval.Month, unitAmount: 4900, stripePriceId: 'price_stripe_pro_month', created: true }],
            draft: saved,
          }),
        }
      : { succeeded: true, response: initial }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<PlanEditor plans={[plan]} versions={[version]} trialsEnabled trialRequiresPaymentMethod={false} defaultTrialDays={14} stripeConfigured stripeCatalogProvisioningEnabled stripeMode="Sandbox" onPlanUpdated={() => {}}/>)
    await waitFor(() => expect(screen.queryByText('Loading plan configuration')).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: /Pricing/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Create missing in Stripe' }))

    await waitFor(() => expect(screen.getByText(/The mappings were saved to the draft; publish it to enable Checkout/)).toBeTruthy())
    expect((screen.getByRole('button', { name: 'Save draft' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Publish changes' }) as HTMLButtonElement).disabled).toBe(false)
    const request = mocks.api.mock.calls.map(([value]) => value).find(value => value instanceof ProvisionSaasPlanStripeCatalog)
    expect(request).toEqual(expect.objectContaining({ planId: 'plan.pro' }))
  })
})
