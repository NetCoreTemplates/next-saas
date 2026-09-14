import { renderHook, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  api: vi.fn(),
}))

vi.mock('@/lib/gateway', () => ({
  client: { api: mocks.api },
}))

import { useSaasDashboard, useUsageAnalytics } from './use-saas'

describe('useSaasDashboard', () => {
  beforeEach(() => {
    mocks.api.mockReset()
    mocks.api.mockResolvedValue({
      succeeded: true,
      response: { workspace: { name: 'Test workspace' } },
    })
  })

  it('loads once and does not refetch when its state causes a render', async () => {
    const { result, rerender } = renderHook(() => useSaasDashboard(), {
      wrapper: StrictMode,
    })

    await waitFor(() => expect(result.current.loading).toBe(false))
    rerender()

    await waitFor(() => expect(result.current.data?.workspace?.name).toBe('Test workspace'))
    expect(mocks.api).toHaveBeenCalledTimes(1)
  })
})

describe('useUsageAnalytics', () => {
  beforeEach(() => {
    mocks.api.mockReset()
    mocks.api.mockResolvedValue({
      succeeded: true,
      response: { series: [], byUser: [], projectedPeriodEndUnits: 0, rejectedOperations: 0 },
    })
  })

  it('does not refetch when loading and response state rerender the consumer', async () => {
    const { result, rerender } = renderHook(
      ({ meter, revision }) => useUsageAnalytics(meter, 30, true, revision),
      { initialProps: { meter: 'api.requests', revision: 'api.requests:0:0' }, wrapper: StrictMode },
    )

    await waitFor(() => expect(result.current.loading).toBe(false))
    for (let i = 0; i < 5; i++) rerender({ meter: 'api.requests', revision: 'api.requests:0:0' })
    expect(mocks.api).toHaveBeenCalledTimes(1)

    rerender({ meter: 'storage.bytes', revision: 'storage.bytes:0:0' })
    await waitFor(() => expect(mocks.api).toHaveBeenCalledTimes(2))
  })
})
