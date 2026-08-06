import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '../components/ui'
import { DormRosterImportPage } from '../pages/DormRosterImportPage'
import type { DormRosterImportPreview } from '../types'

const preview: DormRosterImportPreview = {
  fileName: 'residents.xlsx', rowsRead: 3, ignoredRows: 1, residentCount: 2, occupiedRooms: 2,
  addedResidents: 0, removedResidents: 0, movedResidents: 1, updatedResidents: 0, unchangedResidents: 1,
  canApply: true, issues: [], changes: [{ type: 'Moved', firstName: 'Alex', lastName: 'Lee', fromRoom: 'ELTS-01A', toRoom: 'ELTS-01B' }],
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><ToastProvider><DormRosterImportPage /></ToastProvider></QueryClientProvider>)
}

describe('Resident roster import', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(preview), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('analyzes an Excel file before asking the director to apply changes', async () => {
    renderPage()
    const workbook = new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], 'residents.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
    fireEvent.change(screen.getByLabelText('Excel spreadsheet'), { target: { files: [workbook] } })
    fireEvent.click(screen.getByRole('button', { name: /Analyze spreadsheet/ }))

    expect(await screen.findByRole('heading', { name: 'Roster analysis' })).toBeInTheDocument()
    expect(screen.getByText('Alex Lee')).toBeInTheDocument()
    expect(screen.getByText('ELTS-01A')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/admin/dorm-roster/preview'), expect.objectContaining({ method: 'POST', body: expect.any(FormData) }))

    fireEvent.click(screen.getByRole('button', { name: 'Apply roster changes' }))
    expect(screen.getByRole('dialog', { name: 'Apply resident roster?' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Yes, apply changes' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/admin/dorm-roster/apply'), expect.objectContaining({ method: 'POST', body: expect.any(FormData) })))
  })
})
