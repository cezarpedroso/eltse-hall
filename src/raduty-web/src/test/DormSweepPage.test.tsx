import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { DormSweepPage } from '../pages/DormSweepPage'
import type { DormSweepSuite } from '../types'

const suites: DormSweepSuite[] = [
  { suiteNumber: '01', latestSweep: null },
  {
    suiteNumber: '02',
    latestSweep: {
      id: 'sweep-2',
      checkedByUserId: 'ra-1',
      checkedByName: 'Jordan Lee',
      checkedAt: '2026-08-05T00:00:00Z',
      hasConcerns: true,
    },
  },
]
let resetFailure = false

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><ToastProvider><DormSweepPage /></ToastProvider></QueryClientProvider>)
}

describe('Dorm sweeps', () => {
  beforeEach(() => {
    resetFailure = false
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:sweep-report') })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(JSON.stringify({
          id: 'sweep-1',
          suiteNumber: '01',
          checkedByUserId: 'ra-1',
          checkedByName: 'Jordan Lee',
          checkedAt: '2026-08-06T00:00:00Z',
          isCommonAreaClean: false,
          hasMoldOrLeak: true,
          hasFurnitureMovedToCommonArea: false,
          isBathroomClean: true,
          areToiletsAndSinksWorking: true,
          areShowersWorking: true,
          smellsLikeMarijuanaOrAlcohol: true,
          notes: 'Trash needs pickup.',
          hasConcerns: true,
        }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      }
      if (init?.method === 'DELETE') {
        return Promise.resolve(resetFailure
          ? new Response(JSON.stringify({ status: 500, title: 'Reset failed.', code: 'RESET_FAILED' }), { status: 500, headers: { 'Content-Type': 'application/problem+json' } })
          : new Response(JSON.stringify({ deletedSweeps: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      }
      if (String(_url).includes('/api/dorm-sweeps/pdf')) {
        return Promise.resolve(new Response(new Blob(['%PDF-1.7']), { status: 200, headers: { 'Content-Type': 'application/pdf' } }))
      }
      return Promise.resolve(new Response(JSON.stringify(suites), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('opens a suite-level sweep form and saves common-area answers', async () => {
    renderPage()
    expect(await screen.findByRole('button', { name: /Suite 01/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export PDF' })).toBeInTheDocument()
    expect(screen.getByText('Needs attention')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Suite 01/ }))
    const dialog = screen.getByRole('dialog', { name: 'Suite 01 sweep' })
    expect(within(dialog).getByText('Bathrooms and common area')).toBeInTheDocument()

    const yesAnswers = within(dialog).getAllByLabelText('Yes')
    fireEvent.click(yesAnswers[1])
    fireEvent.click(yesAnswers[6])
    fireEvent.change(within(dialog).getByLabelText('Notes'), { target: { value: 'Trash needs pickup.' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save sweep' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-sweeps/suites/01'), expect.objectContaining({ method: 'POST' })))
    const postCall = vi.mocked(fetch).mock.calls.find(([url, init]) => String(url).includes('/api/dorm-sweeps/suites/01') && init?.method === 'POST')
    expect(JSON.parse(String(postCall?.[1]?.body))).toMatchObject({
      isCommonAreaClean: true,
      hasMoldOrLeak: true,
      smellsLikeMarijuanaOrAlcohol: true,
      notes: 'Trash needs pickup.',
    })
  })

  it('downloads the suite sweep PDF', async () => {
    renderPage()
    await screen.findByRole('button', { name: /Suite 01/ })

    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-sweeps/pdf'), expect.objectContaining({ credentials: 'include' })))
  })

  it('allows every signed-in user to open the reset confirmation, including with no saved sweeps', async () => {
    renderPage()
    await screen.findByRole('button', { name: /Suite 01/ })

    expect(screen.getByRole('button', { name: 'Reset sweeps' })).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: 'Reset sweeps' }))
    expect(screen.getByRole('dialog', { name: 'Reset all dorm sweeps?' })).toBeInTheDocument()
  })

  it('requires confirmation before resetting all hall sweeps and refreshes the suites', async () => {
    renderPage()
    await screen.findByRole('button', { name: /Suite 01/ })

    fireEvent.click(screen.getByRole('button', { name: 'Reset sweeps' }))
    const dialog = screen.getByRole('dialog', { name: 'Reset all dorm sweeps?' })
    expect(within(dialog).getByText(/permanently deletes all saved dorm sweep records for Eltse Hall/i)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, reset all sweeps' }))

    expect(await screen.findByText('1 dorm sweep reset')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-sweeps'), expect.objectContaining({ method: 'DELETE' }))
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([url, init]) => String(url).includes('/api/dorm-sweeps/suites') && init?.method !== 'DELETE')).toHaveLength(2))
  })

  it('shows reset failures and keeps the confirmation dialog open', async () => {
    resetFailure = true
    renderPage()
    await screen.findByRole('button', { name: /Suite 01/ })

    fireEvent.click(screen.getByRole('button', { name: 'Reset sweeps' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yes, reset all sweeps' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Reset failed.')
    expect(screen.getByRole('dialog', { name: 'Reset all dorm sweeps?' })).toBeInTheDocument()
  })
})
