import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { UploadStoredFile } from '@/lib/dtos'

const mocks = vi.hoisted(() => ({
  api: vi.fn(),
  apiForm: vi.fn(),
  refresh: vi.fn(),
}))

vi.mock('@/lib/gateway', () => ({ client: { api: mocks.api, apiForm: mocks.apiForm } }))
vi.mock('@/lib/auth', () => ({ ValidateAuth: (Component: React.ComponentType) => Component }))
vi.mock('@/components/app-shell', async () => {
  const actual = await vi.importActual<typeof import('@/components/app-shell')>('@/components/app-shell')
  return {
    ...actual,
    default: ({ children }: React.PropsWithChildren) => <main>{children}</main>,
    PageHeading: ({ action }: { action?: React.ReactNode }) => <header>{action}</header>,
  }
})
vi.mock('@/components/feature-gate', () => ({ default: ({ children }: React.PropsWithChildren) => <>{children}</> }))
vi.mock('@/lib/use-saas', () => ({
  LoadingPanel: () => <div>loading</div>,
  useSaasDashboard: () => ({
    data: { workspace: { name: 'Acme' }, usage: [], entitlements: [], subscription: { accessMode: 'Full' } },
    loading: false,
    refresh: mocks.refresh,
  }),
}))

import DocumentsPage from './page'

const file = (name: string) => new File(['content'], name, { type: 'text/plain' })

function selectFiles(...names: string[]) {
  const input = document.querySelector('input[type=file]') as HTMLInputElement
  fireEvent.change(input, { target: { files: names.map(file) } })
}

describe('documents upload', () => {
  beforeEach(() => {
    mocks.api.mockReset().mockResolvedValue({ succeeded: true, response: { results: [] } })
    mocks.apiForm.mockReset().mockResolvedValue({ succeeded: true, response: {} })
    mocks.refresh.mockReset()
  })

  it('accepts a multiple-file selection', async () => {
    render(<DocumentsPage />)
    expect((document.querySelector('input[type=file]') as HTMLInputElement).multiple).toBe(true)
  })

  it('uploads every selected file with its own idempotency key', async () => {
    render(<DocumentsPage />)
    selectFiles('first.txt', 'second.txt', 'third.txt')

    await waitFor(() => expect(mocks.apiForm).toHaveBeenCalledTimes(3))
    const requests = mocks.apiForm.mock.calls.map(([request]) => request as UploadStoredFile)
    const keys = requests.map(x => x.idempotencyKey)
    expect(keys.every(Boolean)).toBe(true)
    expect(new Set(keys).size).toBe(3)
    await screen.findByText(/3 files are securely stored/)
  })

  it('reports each rejected file without discarding the files around it', async () => {
    mocks.apiForm
      .mockResolvedValueOnce({ succeeded: true, response: {} })
      .mockResolvedValueOnce({ succeeded: false, error: { message: 'Quota exceeded for documents.stored.' } })
      .mockResolvedValueOnce({ succeeded: true, response: {} })

    render(<DocumentsPage />)
    selectFiles('first.txt', 'too-big.txt', 'third.txt')

    await screen.findByText(/2 of 3 files uploaded/)
    await screen.findByText('Quota exceeded for documents.stored.')
    // The successful uploads leave the queue; only the rejection stays for the customer to act on.
    expect(screen.getByTestId('upload-queue').querySelectorAll('li')).toHaveLength(1)
    expect(screen.getByText('too-big.txt')).toBeTruthy()
  })

  it('uploads files dropped onto the file list', async () => {
    render(<DocumentsPage />)
    fireEvent.drop(screen.getByTestId('drop-zone'), { dataTransfer: { files: [file('dropped.txt')] } })

    await waitFor(() => expect(mocks.apiForm).toHaveBeenCalledTimes(1))
    await screen.findByText(/1 file is securely stored/)
  })

  it('refreshes the file list and usage once per batch', async () => {
    render(<DocumentsPage />)
    await waitFor(() => expect(mocks.api).toHaveBeenCalledTimes(1))
    selectFiles('first.txt', 'second.txt')

    await waitFor(() => expect(mocks.refresh).toHaveBeenCalledTimes(1))
    expect(mocks.api).toHaveBeenCalledTimes(2)
  })
})
