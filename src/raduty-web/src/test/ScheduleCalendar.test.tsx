import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ScheduleCalendar } from '../components/ScheduleCalendar'
import { ToastProvider } from '../components/ui'
import { directorUser, makeSchedule, makeShift, raUser } from './fixtures'

function renderCalendar(schedule = makeSchedule(), user = raUser) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><ToastProvider><ScheduleCalendar schedule={schedule} user={user} /></ToastProvider></QueryClientProvider>)
}

describe('Schedule calendar', () => {
  beforeEach(() => { vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } }))) })
  afterEach(() => vi.unstubAllGlobals())

  it('renders the monthly calendar and open shift state', () => {
    renderCalendar()
    expect(screen.getByRole('region', { name: 'August 2026 duty calendar' })).toBeInTheDocument()
    expect(screen.getByText('Open shift')).toBeInTheDocument()
  })

  it('renders assigned-to-me state and the assigned RA name', () => {
    renderCalendar(makeSchedule([makeShift({ assignments: [{ id: 'a1', userId: raUser.id, firstName: 'Jordan', lastName: 'Lee', status: 'Confirmed', isMine: true }] })]))
    expect(screen.getByRole('button', { name: /Assigned to you. Jordan Lee/ })).toBeInTheDocument()
    expect(screen.getByText('Jordan Lee').closest('.calendar-event')).toHaveClass('calendar-event--mine')
  })

  it('selects an open shift without a lifecycle status gate', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response(JSON.stringify({ id: 'a1' }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    renderCalendar({ ...makeSchedule(), status: 'Draft' })
    fireEvent.click(screen.getAllByRole('button', { name: /Monday, August 3, 2026/ })[0])
    fireEvent.click(screen.getByRole('button', { name: 'Select this shift' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/shifts/shift-1/assignments/me'), expect.objectContaining({ method: 'POST' })))
  })

  it('allows the current user to remove only their own assignment', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 204 }))
    const mine = makeShift({ assignments: [{ id: 'a1', userId: raUser.id, firstName: 'Jordan', lastName: 'Lee', status: 'Confirmed', isMine: true }] })
    renderCalendar(makeSchedule([mine]))
    fireEvent.click(screen.getByRole('button', { name: /Assigned to you. Jordan Lee/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove my assignment' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/shifts/shift-1/assignments/me'), expect.objectContaining({ method: 'DELETE' })))
  })

  it('renders a stable API rule error', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response(JSON.stringify({ status: 422, title: 'The monthly shift limit has been reached.', code: 'MAXIMUM_SHIFTS_REACHED' }), { status: 422, headers: { 'Content-Type': 'application/problem+json' } }))
    renderCalendar()
    fireEvent.click(screen.getAllByRole('button', { name: /Monday, August 3, 2026/ })[0])
    fireEvent.click(screen.getByRole('button', { name: 'Select this shift' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('monthly shift limit')
  })

  it('allows a Hall Director to assign a staff member', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response(JSON.stringify([{ id: 'ra-2', firstName: 'Jennie', lastName: 'Robison', schoolEmail: 'jennie@wmpenn.edu', role: 'ResidentAssistant', isActive: true, shiftCount: 0 }]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'assignment-2' }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    renderCalendar(makeSchedule(), directorUser)
    fireEvent.click(screen.getAllByRole('button', { name: /Monday, August 3, 2026/ })[0])
    const staff = await screen.findByLabelText('Assign staff member')
    await screen.findByRole('option', { name: 'Jennie Robison' })
    fireEvent.change(staff, { target: { value: 'ra-2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Assign shift' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/admin/shifts/shift-1/assignments'), expect.objectContaining({ method: 'POST', body: expect.stringContaining('ra-2') })))
  })

  it('allows a Hall Director to unassign another person', async () => {
    const assigned = makeShift({ assignments: [{ id: 'a1', userId: raUser.id, firstName: 'Jordan', lastName: 'Lee', status: 'Confirmed', isMine: false }] })
    renderCalendar(makeSchedule([assigned]), directorUser)
    fireEvent.click(screen.getByRole('button', { name: /Jordan Lee/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Unassign Jordan Lee' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/admin/shifts/shift-1/assignments/a1'), expect.objectContaining({ method: 'DELETE' })))
  })

  it('has no automatically detectable accessibility violations', async () => {
    const { container } = renderCalendar()
    const results = await axe.run(container)
    expect(results.violations).toEqual([])
  })
})
