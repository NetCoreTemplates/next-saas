import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RequestWorkspaceDeletion } from '@/lib/dtos'

const mocks = vi.hoisted(() => ({ api: vi.fn() }))

vi.mock('@/lib/gateway', () => ({ client: { api: mocks.api, get: vi.fn() } }))
vi.mock('@/components/app-shell', () => ({
  Panel: ({ children, className }: React.PropsWithChildren<{ className?: string }>) => <div className={className}>{children}</div>,
  StatusPill: ({ children }: React.PropsWithChildren) => <span>{children}</span>,
}))

import WorkspaceLifecycle from './workspace-lifecycle'

describe('WorkspaceLifecycle', () => {
  beforeEach(() => {
    mocks.api.mockReset()
    mocks.api.mockResolvedValue({ succeeded: true, response: { results: [], exports: [] } })
  })

  it('schedules deletion with organization-name confirmation and no password prompt', async () => {
    render(<WorkspaceLifecycle workspaceName="Northstar Labs" role="Owner" />)

    expect(screen.queryByLabelText('Current password')).toBeNull()
    const button = screen.getByRole('button', { name: 'Schedule deletion' }) as HTMLButtonElement
    expect(button.disabled).toBe(false)

    fireEvent.change(screen.getByLabelText('Organization name confirmation'), { target: { value: 'Northstar Labs' } })
    fireEvent.click(button)

    await waitFor(() => {
      const request = mocks.api.mock.calls.map(([value]) => value).find(value => value instanceof RequestWorkspaceDeletion)
      expect(request).toMatchObject({ confirmation: 'Northstar Labs' })
      expect(request).not.toHaveProperty('currentPassword')
    })
  })

  it('shows the size, expiry, and checksum for a ready export', async () => {
    mocks.api.mockResolvedValue({
      succeeded: true,
      response: {
        results: [],
        exports: [{
          id: 'export-1',
          byteLength: 1536,
          sha256: '0123456789abcdef',
          expiresAt: '2026-09-30T09:00:00.000Z',
        }],
      },
    })

    render(<WorkspaceLifecycle workspaceName="Northstar Labs" role="Owner" />)

    expect(await screen.findByText('Ready to download')).toBeDefined()
    expect(screen.getByText(/1\.5 KB · Expires/)).toBeDefined()
    expect(screen.getByText('SHA-256 0123456789abcdef')).toBeDefined()
  })
})
