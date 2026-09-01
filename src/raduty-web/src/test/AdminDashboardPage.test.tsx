import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { AdminDashboardPage } from '../pages/AdminPages'
import { makeSchedule, raUser } from './fixtures'

describe('Hall Director schedule desk', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('focuses on managing RAs and supports schedules two months ahead', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      const body = url.includes('/admin/users') ? [{ ...raUser, shiftCount: 2 }] : makeSchedule()
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(<MemoryRouter><QueryClientProvider client={client}><ToastProvider><AdminDashboardPage /></ToastProvider></QueryClientProvider></MemoryRouter>)

    expect(await screen.findByRole('heading', { level: 1, name: 'Resident Assistants' })).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Search by name or room')).toBeInTheDocument()
    expect(await screen.findByText('Jordan Lee')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Manage RAs/i })).toHaveAttribute('href', '/admin/users')
    expect(screen.queryByText('Assignment distribution')).not.toBeInTheDocument()
    expect(screen.queryByText('Weekend assignments')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous month' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Next month' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next month' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled())
  })
})
