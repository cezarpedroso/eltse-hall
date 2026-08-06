import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { SchedulePage } from '../pages/SchedulePage'
import { makeSchedule, raUser } from './fixtures'

describe('Schedule page month boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads only the actual current month and provides no month navigation', async () => {
    const now = new Date()
    const month = now.getMonth() + 1
    const year = now.getFullYear()
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      const body = url.endsWith('/summary')
        ? { totalShifts: 31, openShifts: 10, unfilledPositions: 10, myShiftCount: 2, myWeekendShiftCount: 1, myUpcomingShifts: [] }
        : { ...makeSchedule(), year, month }
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(<MemoryRouter><QueryClientProvider client={client}><ToastProvider><SchedulePage user={raUser} /></ToastProvider></QueryClientProvider></MemoryRouter>)

    expect(await screen.findByText('Selection open')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining(`/api/schedules/${year}/${month}`), expect.anything()))
    expect(screen.queryByRole('button', { name: 'Previous month' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next month' })).not.toBeInTheDocument()
  })
})
