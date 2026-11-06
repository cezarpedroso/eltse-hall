import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../components/ui'
import { ResidentManagementPage } from '../pages/ResidentManagementPage'
import { directorUser, raUser } from './fixtures'

const resident = { id: 'resident-1', firstName: 'Alex', lastName: 'Rivera', dormRoomId: 'room-a', roomCode: 'ELTS-01A', sportOrActivity: 'Soccer' }
const rooms = [
  { id: 'room-a', roomCode: 'ELTS-01A', occupancy: 1, capacity: 2 },
  { id: 'room-b', roomCode: 'ELTS-01B', occupancy: 0, capacity: 2 },
]

function renderPage(user = raUser) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<MemoryRouter><QueryClientProvider client={client}><ToastProvider><ResidentManagementPage user={user} /></ToastProvider></QueryClientProvider></MemoryRouter>)
}

describe('Resident management', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: string, init?: RequestInit) => {
      const url = String(input)
      if (init?.method === 'PUT') return Promise.resolve(new Response(JSON.stringify({ ...resident, dormRoomId: 'room-b', roomCode: 'ELTS-01B' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      if (init?.method === 'DELETE') return Promise.resolve(url.endsWith('/api/residents')
        ? new Response(JSON.stringify({ deletedResidents: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response(null, { status: 204 }))
      const body = url.endsWith('/api/residents/rooms') ? rooms : [resident]
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('lets an RA move a resident to another room in their hall', async () => {
    renderPage()
    expect(await screen.findByText('Alex Rivera')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Import Excel' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Edit or move' }))
    fireEvent.change(screen.getByLabelText(/Eltse Hall room/), { target: { value: 'room-b' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/residents/resident-1'), expect.objectContaining({ method: 'PUT', body: expect.stringContaining('room-b') })))
  })

  it('requires confirmation before moving a resident to another dorm', async () => {
    renderPage()
    await screen.findByText('Alex Rivera')
    fireEvent.click(screen.getByRole('button', { name: 'Edit or move' }))
    fireEvent.click(screen.getByRole('button', { name: 'Move to another dorm' }))
    expect(screen.getByRole('dialog', { name: 'Move Alex to another dorm?' })).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalledWith(expect.stringContaining('/api/residents/resident-1'), expect.objectContaining({ method: 'DELETE' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yes, remove from Eltse Hall' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/residents/resident-1'), expect.objectContaining({ method: 'DELETE' })))
  })

  it('does not expose campus roster import to a Hall Director', async () => {
    renderPage(directorUser)
    await screen.findByText('Residents')
    expect(screen.queryByRole('link', { name: 'Import Excel' })).not.toBeInTheDocument()
  })

  it('requires an authorized leader to confirm before clearing the active hall roster', async () => {
    renderPage(directorUser)
    await screen.findByText('Alex Rivera')
    fireEvent.click(screen.getByRole('button', { name: 'Clear residents' }))
    expect(screen.getByRole('dialog', { name: 'Clear Eltse Hall residents?' })).toBeInTheDocument()
    expect(screen.getByText(/remove all 1 resident from Eltse Hall/i)).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalledWith(expect.stringMatching(/\/api\/residents$/), expect.objectContaining({ method: 'DELETE' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yes, clear Eltse Hall' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringMatching(/\/api\/residents$/), expect.objectContaining({ method: 'DELETE' })))
  })

  it('does not show the clear-roster action to an RA', async () => {
    renderPage()
    await screen.findByText('Residents')
    expect(screen.queryByRole('button', { name: 'Clear residents' })).not.toBeInTheDocument()
  })
})
