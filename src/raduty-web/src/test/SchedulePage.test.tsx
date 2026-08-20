import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { SchedulePage } from '../pages/SchedulePage'
import { makeSchedule, raUser } from './fixtures'

describe('Schedule page month window', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('allows the current month and the next two months only', async () => {
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

    await waitFor(() => expect(screen.getByRole('button', { name: 'Download schedule PDF' })).toBeEnabled())
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining(`/api/schedules/${year}/${month}`), expect.anything()))
    expect(screen.getByRole('button', { name: 'Previous month' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next month' })).toBeEnabled()

    const next = new Date(year, month, 1)
    fireEvent.click(screen.getByRole('button', { name: 'Next month' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining(`/api/schedules/${next.getFullYear()}/${next.getMonth() + 1}`), expect.anything()))

    const final = new Date(year, month + 1, 1)
    fireEvent.click(screen.getByRole('button', { name: 'Next month' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining(`/api/schedules/${final.getFullYear()}/${final.getMonth() + 1}`), expect.anything()))
    expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Previous month' })).toBeEnabled()
  })
})
