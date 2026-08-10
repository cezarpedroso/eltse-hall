import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowRightLeft, FileSpreadsheet, Plus, Search, UserMinus } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, api } from '../api'
import { Dialog, EmptyState, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'
import { refreshResidentData, residentQueryKeys, sharedResidentQueryOptions } from '../residentData'
import type { CurrentUser, DormRoomOption, ManagedDormResident } from '../types'

type ResidentForm = { firstName: string; lastName: string; dormRoomId: string; sportOrActivity: string }
const emptyForm: ResidentForm = { firstName: '', lastName: '', dormRoomId: '', sportOrActivity: '' }

export function ResidentManagementPage({ user }: { user: CurrentUser }) {
  const [search, setSearch] = useState('')
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<ManagedDormResident | null>(null)
  const [transferring, setTransferring] = useState<ManagedDormResident | null>(null)
  const residents = useQuery({ queryKey: residentQueryKeys.residents, queryFn: ({ signal }) => api<ManagedDormResident[]>('/api/residents', {}, signal), ...sharedResidentQueryOptions })
  const rooms = useQuery({ queryKey: residentQueryKeys.rooms, queryFn: ({ signal }) => api<DormRoomOption[]>('/api/residents/rooms', {}, signal), ...sharedResidentQueryOptions })
  const queryClient = useQueryClient()
  const toast = useToast()
  const remove = useMutation({
    mutationFn: (residentId: string) => api<void>(`/api/residents/${residentId}`, { method: 'DELETE' }),
    onSuccess: async () => {
      toast(`${transferring?.firstName ?? 'Resident'} removed from the Eltse Hall roster.`)
      setTransferring(null)
      await refreshResidentData(queryClient)
    },
  })
  const term = search.trim().toLowerCase()
  const visible = residents.data?.filter((resident) => !term || `${resident.firstName} ${resident.lastName} ${resident.roomCode} ${resident.sportOrActivity ?? ''}`.toLowerCase().includes(term)) ?? []
  const canImport = user.role === 'HallDirector' || user.role === 'Admin'

  return <div className="page resident-management-page">
    <header className="resident-management-heading"><div><span className="eyebrow">Eltse Hall</span><h1>Residents</h1><p>Add residents, correct their information, or move them when room assignments change.</p></div><div className="resident-heading-actions">{canImport && <Link className="button button--quiet" to="/admin/residents"><FileSpreadsheet size={17} />Import Excel</Link>}<button type="button" className="button button--primary" onClick={() => setCreating(true)}><Plus size={17} />Add resident</button></div></header>

    <div className="resident-toolbar"><label className="search-field"><Search size={18} /><span className="sr-only">Search residents</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search name, room, or activity" /></label><span>{visible.length} of {residents.data?.length ?? 0} residents</span></div>
    {residents.isLoading && <div className="resident-list-skeleton"><div className="skeleton skeleton--title" />{Array.from({ length: 6 }, (_, index) => <div className="skeleton skeleton--cell" key={index} />)}</div>}
    {residents.isError && <ErrorState title="Residents unavailable" message={residents.error instanceof ApiError ? residents.error.problem.title : 'The resident list could not be loaded.'} onRetry={() => residents.refetch()} />}
    {residents.data && visible.length === 0 && <EmptyState title="No residents found" message={term ? 'Try a different name or room.' : 'Add the first resident to Eltse Hall.'} />}
    {visible.length > 0 && <div className="resident-table responsive-table"><table><thead><tr><th>Resident</th><th>Room</th><th>Sport / activity</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{visible.map((resident) => <tr key={resident.id}><td data-label="Resident"><span className="resident-name"><span>{initials(resident)}</span><strong>{resident.firstName} {resident.lastName}</strong></span></td><td data-label="Room"><span className="resident-room"><ArrowRightLeft size={14} />{resident.roomCode}</span></td><td data-label="Sport / activity">{resident.sportOrActivity ?? '—'}</td><td><button type="button" className="text-button" onClick={() => setEditing(resident)}>Edit or move</button></td></tr>)}</tbody></table></div>}

    <ResidentEditorDialog open={creating || !!editing} resident={editing} rooms={rooms.data ?? []} onClose={() => { setCreating(false); setEditing(null) }} onTransfer={(resident) => { setEditing(null); setCreating(false); setTransferring(resident) }} />
    <Dialog open={!!transferring} onClose={() => !remove.isPending && setTransferring(null)} title={transferring ? `Move ${transferring.firstName} to another dorm?` : 'Move resident to another dorm?'} className="resident-transfer-dialog"><div className="resident-transfer-warning"><UserMinus /><div><strong>Remove this resident from Eltse Hall?</strong><p>The resident will disappear from Eltse room assignments. Existing dorm-check records will remain attached to the rooms.</p></div></div>{remove.isError && <p className="inline-error" role="alert">{remove.error instanceof ApiError ? remove.error.problem.title : 'The resident could not be removed.'}</p>}<div className="dialog-actions"><button type="button" className="button button--danger" onClick={() => transferring && remove.mutate(transferring.id)} disabled={remove.isPending}>{remove.isPending ? 'Removing…' : 'Yes, remove from Eltse'}</button><button type="button" className="button button--quiet" onClick={() => setTransferring(null)} disabled={remove.isPending}>Cancel</button></div></Dialog>
  </div>
}

function ResidentEditorDialog({ open, resident, rooms, onClose, onTransfer }: { open: boolean; resident: ManagedDormResident | null; rooms: DormRoomOption[]; onClose: () => void; onTransfer: (resident: ManagedDormResident) => void }) {
  const [form, setForm] = useState<ResidentForm>(emptyForm)
  const queryClient = useQueryClient()
  const toast = useToast()
  useEffect(() => {
    if (!open) return
    setForm(resident ? { firstName: resident.firstName, lastName: resident.lastName, dormRoomId: resident.dormRoomId, sportOrActivity: resident.sportOrActivity ?? '' }
      : { ...emptyForm, dormRoomId: rooms.find((room) => room.occupancy < room.capacity)?.id ?? '' })
  }, [open, resident, rooms])
  const save = useMutation({
    mutationFn: () => api<ManagedDormResident>(resident ? `/api/residents/${resident.id}` : '/api/residents', { method: resident ? 'PUT' : 'POST', body: JSON.stringify({ ...form, sportOrActivity: form.sportOrActivity.trim() || null }) }),
    onSuccess: async (saved) => {
      toast(`${saved.firstName} ${saved.lastName} ${resident ? 'updated' : 'added'}.`)
      onClose()
      await refreshResidentData(queryClient)
    },
  })
  function submit(event: FormEvent) { event.preventDefault(); save.mutate() }
  return <Dialog open={open} onClose={() => !save.isPending && onClose()} title={resident ? `Edit ${resident.firstName} ${resident.lastName}` : 'Add resident'} className="resident-editor-dialog"><form className="resident-editor-form" onSubmit={submit}><div className="resident-form-grid"><label className="field"><span>First name</span><input required maxLength={80} value={form.firstName} onChange={(event) => setForm({ ...form, firstName: event.target.value })} /></label><label className="field"><span>Last name</span><input required maxLength={80} value={form.lastName} onChange={(event) => setForm({ ...form, lastName: event.target.value })} /></label><label className="field field--wide"><span>Eltse room</span><select required value={form.dormRoomId} onChange={(event) => setForm({ ...form, dormRoomId: event.target.value })}><option value="" disabled>Select a room</option>{rooms.map((room) => <option value={room.id} disabled={room.occupancy >= room.capacity && room.id !== resident?.dormRoomId} key={room.id}>{room.roomCode} · {room.occupancy}/{room.capacity}{room.occupancy >= room.capacity && room.id !== resident?.dormRoomId ? ' full' : ''}</option>)}</select><small>Changing this room moves the resident within Eltse Hall.</small></label><label className="field field--wide"><span>Sport or activity <small>Optional</small></span><input maxLength={120} value={form.sportOrActivity} onChange={(event) => setForm({ ...form, sportOrActivity: event.target.value })} /></label></div>{resident && <div className="resident-move-out"><div><strong>Moving to another dorm?</strong><span>Remove this resident from the Eltse roster.</span></div><button type="button" className="button button--danger-quiet" onClick={() => onTransfer(resident)}><UserMinus size={16} />Move to another dorm</button></div>}{save.isError && <p className="inline-error" role="alert">{save.error instanceof ApiError ? save.error.problem.title : 'The resident could not be saved.'}</p>}<div className="dialog-actions"><button type="submit" className="button button--primary" disabled={save.isPending || !form.dormRoomId}>{save.isPending ? 'Saving…' : resident ? 'Save changes' : 'Add resident'}</button><button type="button" className="button button--quiet" onClick={onClose} disabled={save.isPending}>Cancel</button></div></form></Dialog>
}

function initials(resident: ManagedDormResident) { return `${resident.firstName[0] ?? ''}${resident.lastName[0] ?? ''}` }
