import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { AdminDashboardPage } from '../pages/AdminPages'
import { makeSchedule } from './fixtures'

describe('Hall Director schedule desk', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('uses live schedules and supports the current month plus two months ahead', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      const body = url.includes('/unfilled') || url.includes('/distribution')
        ? []
        : url.includes('/audit-logs')
          ? { items: [], page: 1, pageSize: 6, total: 0 }
          : makeSchedule()
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(<MemoryRouter><QueryClientProvider client={client}><ToastProvider><AdminDashboardPage /></ToastProvider></QueryClientProvider></MemoryRouter>)

    expect(await screen.findByText('Live schedule')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /publish schedule/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /draft/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous month' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Next month' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next month' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled())
  })
})
