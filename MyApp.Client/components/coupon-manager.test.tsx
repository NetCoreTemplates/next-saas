import { render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { GetSaasCouponsResponse } from '@/lib/dtos'

const mocks = vi.hoisted(() => ({ api: vi.fn() }))

vi.mock('@/lib/gateway', () => ({ client: { api: mocks.api } }))
vi.mock('@/components/app-shell', () => ({
  Panel: ({ children, className }: React.PropsWithChildren<{ className?: string }>) => <div className={className}>{children}</div>,
  StatusPill: ({ children }: React.PropsWithChildren) => <span>{children}</span>,
}))

import { CouponManager } from './coupon-manager'

describe('CouponManager', () => {
  beforeEach(() => {
    mocks.api.mockReset()
    mocks.api.mockResolvedValue({
      succeeded: true,
      response: new GetSaasCouponsResponse({ stripeConfigured: false, results: [] }),
    })
  })

  it('loads once in React Strict Mode and shows Stripe setup guidance', async () => {
    render(<StrictMode><CouponManager/></StrictMode>)

    await waitFor(() => expect(screen.getByText('Connect Stripe to manage discounts')).toBeTruthy())
    expect(mocks.api).toHaveBeenCalledTimes(1)
  })
})
