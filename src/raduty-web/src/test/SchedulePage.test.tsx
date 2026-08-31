import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { SchedulePage } from '../pages/SchedulePage'
import { directorUser, makeSchedule, raUser } from './fixtures'

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

  it('lets a Hall Director start a missing schedule from the calendar', async () => {
    const now = new Date()
    const month = now.getMonth() + 1
    const year = now.getFullYear()
    const created = { ...makeSchedule(), year, month }
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (init?.method === 'POST' && url.includes('/api/admin/schedules/'))
        return Promise.resolve(new Response(JSON.stringify(created), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      if (url.endsWith('/summary'))
        return Promise.resolve(new Response(JSON.stringify({ totalShifts: 31, openShifts: 31, unfilledPositions: 31, myShiftCount: 0, myWeekendShiftCount: 0, myUpcomingShifts: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      return Promise.resolve(new Response(JSON.stringify({ status: 404, title: 'This schedule has not been started.', code: 'SCHEDULE_NOT_FOUND' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(<MemoryRouter><QueryClientProvider client={client}><ToastProvider><SchedulePage user={directorUser} /></ToastProvider></QueryClientProvider></MemoryRouter>)

    const start = await screen.findByRole('button', { name: 'Start schedule' })
    fireEvent.click(start)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/admin/schedules/${year}/${month}/generate`),
      expect.objectContaining({ method: 'POST' }),
    ))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Download schedule PDF' })).toBeEnabled())
    expect(screen.getByText(`${new Date(year, month - 1, 1).toLocaleString('en-US', { month: 'long' })} schedule started.`)).toBeInTheDocument()
  })
})
