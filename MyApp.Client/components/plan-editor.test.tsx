import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  BillingInterval,
  PlanVersionStatus,
  ProvisionSaasPlanStripeCatalog,
  ProvisionSaasPlanStripeCatalogResponse,
  SaasPlan,
  SaasPlanDetails,
  SaasPlanPrice,
  SaasPlanVersion,
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
