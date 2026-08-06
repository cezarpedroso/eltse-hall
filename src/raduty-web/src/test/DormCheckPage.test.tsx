import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { DormCheckPage } from '../pages/DormCheckPage'
import type { DormSuite } from '../types'

const suites: DormSuite[] = [{
  suiteNumber: '01', rooms: [
    { id: 'room-a', roomCode: 'ELTS-01A', roomLetter: 'A', residents: [{ id: 'r1', firstName: 'Alex', lastName: 'Rivera' }, { id: 'r2', firstName: 'Sam', lastName: 'Lee' }], latestCheck: { id: 'existing-check', checkedByUserId: 'ra-1', checkedByName: 'Jordan Lee', checkedAt: '2026-08-05T00:00:00Z', photoCount: 1 } },
    { id: 'room-b', roomCode: 'ELTS-01B', roomLetter: 'B', residents: [] },
    { id: 'room-c', roomCode: 'ELTS-01C', roomLetter: 'C', residents: [] },
    { id: 'room-d', roomCode: 'ELTS-01D', roomLetter: 'D', residents: [] },
  ]
}]

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><ToastProvider><DormCheckPage /></ToastProvider></QueryClientProvider>)
}

describe('Dorm checks', () => {
  beforeEach(() => {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:room-picture') })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') return Promise.resolve(new Response(JSON.stringify({ id: 'check-1' }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      if (init?.method === 'DELETE') return Promise.resolve(new Response(JSON.stringify({ deletedChecks: 1, deletedPhotos: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      return Promise.resolve(new Response(JSON.stringify(suites), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('shows all four rooms, shared residents, and submits a checklist with a picture', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: '01' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export PDF' })).toBeInTheDocument()
    expect(screen.queryByText('Alex Rivera')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Suite 01/ }))
    expect(screen.getByText('Alex Rivera')).toBeInTheDocument()
    expect(screen.getByText('Sam Lee')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Room [A-D]/ })).toHaveLength(4)

    fireEvent.click(screen.getByRole('button', { name: /Room A/ }))
    expect(screen.getByRole('dialog', { name: 'ELTS-01A check' })).toBeInTheDocument()
    screen.getAllByLabelText('Yes').forEach((option) => fireEvent.click(option))
    fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'No issues.' } })
    const picture = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])], 'room.png', { type: 'image/png' })
    fireEvent.change(screen.getByLabelText('Upload pictures'), { target: { files: [picture] } })
    expect(screen.getByAltText('room.png')).toHaveAttribute('src', 'blob:room-picture')
    fireEvent.click(screen.getByRole('button', { name: 'Complete room check' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-checks/rooms/room-a'), expect.objectContaining({ method: 'POST' })))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-checks/checks/check-1/photos'), expect.objectContaining({ method: 'POST', body: expect.any(FormData) })))
  })

  it('requires a second confirmation before resetting all checks', async () => {
    renderPage()
    await screen.findByRole('heading', { name: '01' })
    const resetButton = screen.getByRole('button', { name: 'Reset checks' })
    expect(resetButton).toBeEnabled()

    fireEvent.click(resetButton)
    expect(screen.getByRole('dialog', { name: 'Reset all dorm checks?' })).toBeInTheDocument()
    expect(screen.getByText('Are you sure?')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalledWith(expect.stringContaining('/api/dorm-checks'), expect.objectContaining({ method: 'DELETE' }))

    fireEvent.click(screen.getByRole('button', { name: 'Yes, reset all checks' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/dorm-checks'), expect.objectContaining({ method: 'DELETE' })))
  })
})
