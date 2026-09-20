import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BillingInterval, CreateCheckoutSession, GetSaasPlans, PlanAudience, PlanInfo, PlanPriceInfo } from '@/lib/dtos'

const mocks = vi.hoisted(() => {
  process.env.seedPlans = JSON.stringify([{
    Code: 'pro',
    Name: 'Pro',
    Description: 'Fallback plan copy',
    Monthly: 4900,
    Annual: 49000,
    Features: [],
  }])
  return { api: vi.fn(), user: { id: 'user-1' } }
})

vi.mock('@/lib/gateway', () => ({ client: { api: mocks.api } }))
vi.mock('@/lib/auth', () => ({ appAuth: () => ({ user: mocks.user }) }))
vi.mock('@/components/layout', () => ({ default: ({ children }: React.PropsWithChildren) => <main>{children}</main> }))
vi.mock('next/link', () => ({ default: ({ children, href, ...props }: React.PropsWithChildren<{ href: string }>) => <a href={href} {...props}>{children}</a> }))

import PricingPage from './page'

function livePlan(priceId: string, unitAmount = 4900, checkoutReady = true) {
  return new PlanInfo({
    id: 'plan.pro',
    code: 'pro',
    name: 'Pro',
    description: 'Live plan copy',
    isContactSales: false,
    features: [],
    quotas: [],
    prices: [new PlanPriceInfo({
      id: priceId,
      currency: 'usd',
      interval: BillingInterval.Month,
      unitAmount,
      checkoutReady,
    })],
  })
}

function getRequestCalls<T>(type: new (...args: never[]) => T) {
  return mocks.api.mock.calls.map(([request]) => request).filter(request => request instanceof type) as T[]
}

describe('PricingPage checkout catalog', () => {
  beforeEach(() => {
    mocks.api.mockReset()
  })

  it('shows Personal and Business plans with billing controls beside the cards', async () => {
    const makePlan = (name: string, audience: PlanAudience, monthly: number, annual: number) => new PlanInfo({
      code: name.toLowerCase(), name, audience, description: `${name} plan`, features: [], quotas: [],
      prices: [
        new PlanPriceInfo({ interval: BillingInterval.Month, currency: 'usd', unitAmount: monthly }),
        new PlanPriceInfo({ interval: BillingInterval.Year, currency: 'usd', unitAmount: annual }),
      ],
    })
    mocks.api.mockResolvedValue({ succeeded: true, response: { results: [
      makePlan('Personal Plus', PlanAudience.Individual, 1200, 12000),
      makePlan('Business Plus', PlanAudience.Business, 2500, 25000),
    ] } })

    render(<PricingPage/>)

    await screen.findByText('Personal Plus')
    const planType = screen.getByRole('group', { name: 'Plan type' })
    const billingInterval = screen.getByRole('group', { name: 'Billing interval' })
    expect(planType.closest('section')).toBe(billingInterval.closest('section'))
    expect(planType.closest('section')?.className).toContain('bg-[#f6f8fb]')
    expect(screen.queryByText('Business Plus')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Business' }))
    expect(await screen.findByText('Business Plus')).toBeTruthy()
    expect(screen.queryByText('Personal Plus')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Annual/ }))
    expect(screen.getByText('$21')).toBeTruthy()
  })

  it('does not submit a paid fallback price while the live catalog is pending', () => {
    mocks.api.mockReturnValue(new Promise(() => {}))

    render(<PricingPage/>)

    const checkout = screen.getByRole('button', { name: 'Try Pro' }) as HTMLButtonElement
    expect(checkout.disabled).toBe(true)
    expect(screen.getByText('Live pricing is still loading.')).toBeTruthy()
    fireEvent.click(checkout)
    expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(0)
    expect(getRequestCalls(GetSaasPlans)).toHaveLength(1)
  })

  it('keeps checkout disabled and offers a retry when the catalog fails to load', async () => {
    mocks.api.mockResolvedValue({ succeeded: false, error: { message: 'Network unavailable' } })

    render(<PricingPage/>)

    await waitFor(() => expect(screen.getByText(/Checkout is temporarily unavailable because live pricing could not be loaded/)).toBeTruthy())
    expect((screen.getByRole('button', { name: 'Try Pro' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy()
    expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(0)
  })

  it('submits the current live price ID after the catalog loads', async () => {
    mocks.api.mockImplementation((request) => request instanceof GetSaasPlans
      ? Promise.resolve({ succeeded: true, response: { results: [livePlan('price.pro.v2.month')] } })
      : Promise.resolve({ succeeded: false, error: { message: 'Stop before redirect' } }))

    render(<PricingPage/>)

    await waitFor(() => expect((screen.getByRole('button', { name: 'Try Pro' }) as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: 'Try Pro' }))

    await waitFor(() => expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(1))
    expect(getRequestCalls(CreateCheckoutSession)[0].priceId).toBe('price.pro.v2.month')
  })

  it('does not submit a live price that is not checkout ready', async () => {
    mocks.api.mockResolvedValue({ succeeded: true, response: { results: [livePlan('price.pro.unconfigured', 4900, false)] } })

    render(<PricingPage/>)

    const checkout = await screen.findByRole('button', { name: 'Try Pro' }) as HTMLButtonElement
    await waitFor(() => expect(checkout.title).toBe('This price is not available for checkout.'))
    expect(checkout.disabled).toBe(true)
    fireEvent.click(checkout)
    expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(0)
  })

  it('refreshes the catalog after PlanNotAvailable and asks the customer to retry', async () => {
    let catalogLoads = 0
    mocks.api.mockImplementation((request) => {
      if (request instanceof GetSaasPlans) {
        catalogLoads++
        const plan = catalogLoads === 1
          ? livePlan('price.pro.v1.month')
          : livePlan('price.pro.v2.month', 6900)
        return Promise.resolve({ succeeded: true, response: { results: [plan] } })
      }
      return Promise.resolve({ succeeded: false, error: { errorCode: 'PlanNotAvailable', message: 'Retired version' } })
    })

    render(<PricingPage/>)

    await waitFor(() => expect((screen.getByRole('button', { name: 'Try Pro' }) as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: 'Try Pro' }))

    await waitFor(() => expect(screen.getByText('Pricing changed while this page was open. Review the latest plans and retry checkout.')).toBeTruthy())
    expect(getRequestCalls(GetSaasPlans)).toHaveLength(2)
    expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(1)
    expect(getRequestCalls(CreateCheckoutSession)[0].priceId).toBe('price.pro.v1.month')
  })

  it('replaces a retired v1 price with the refreshed v2 price without automatically retrying', async () => {
    let catalogLoads = 0
    let checkoutCalls = 0
    mocks.api.mockImplementation((request) => {
      if (request instanceof GetSaasPlans) {
        catalogLoads++
        return Promise.resolve({
          succeeded: true,
          response: { results: [catalogLoads === 1 ? livePlan('price.pro.v1.month') : livePlan('price.pro.v2.month', 6900)] },
        })
      }
      checkoutCalls++
      return Promise.resolve(checkoutCalls === 1
        ? { succeeded: false, error: { errorCode: 'PlanNotAvailable', message: 'Retired version' } }
        : { succeeded: false, error: { message: 'Stop before redirect' } })
    })

    render(<PricingPage/>)

    await waitFor(() => expect((screen.getByRole('button', { name: 'Try Pro' }) as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: 'Try Pro' }))
    await screen.findByText('Pricing changed while this page was open. Review the latest plans and retry checkout.')

    expect(screen.getByText('$69')).toBeTruthy()
    expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: 'Try Pro' }))
    await waitFor(() => expect(getRequestCalls(CreateCheckoutSession)).toHaveLength(2))
    expect(getRequestCalls(CreateCheckoutSession)[1].priceId).toBe('price.pro.v2.month')
  })
})
