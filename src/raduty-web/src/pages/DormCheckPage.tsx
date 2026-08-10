import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Camera, Check, ChevronDown, ChevronRight, ClipboardCheck, Download, ImagePlus, RotateCcw, Trash2, UserRound } from 'lucide-react'
import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { api, ApiError, downloadPdf } from '../api'
import { Dialog, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'
import { residentQueryKeys, sharedResidentQueryOptions } from '../residentData'
import type { DormCheckPhoto, DormCheckReset, DormRoom, DormRoomCheck, DormSuite } from '../types'

export function DormCheckPage() {
  const suites = useQuery({ queryKey: residentQueryKeys.dormCheckSuites, queryFn: ({ signal }) => api<DormSuite[]>('/api/dorm-checks/suites', {}, signal), ...sharedResidentQueryOptions })
  const [selectedRoom, setSelectedRoom] = useState<DormRoom | null>(null)
  const [openSuite, setOpenSuite] = useState<string | null>(null)
  const [confirmReset, setConfirmReset] = useState(false)
  const [exporting, setExporting] = useState(false)
  const queryClient = useQueryClient()
  const toast = useToast()
  const rooms = suites.data?.flatMap((suite) => suite.rooms) ?? []
  const complete = rooms.filter((room) => room.latestCheck).length
  async function exportPdf() {
    setExporting(true)
    try { await downloadPdf('/api/dorm-checks/pdf', 'eltse-hall-dorm-checks.pdf') }
    catch (error) { toast(error instanceof ApiError ? error.problem.title : 'The PDF could not be downloaded.', 'error') }
    finally { setExporting(false) }
  }
  const reset = useMutation({
    mutationFn: () => api<DormCheckReset>('/api/dorm-checks', { method: 'DELETE' }),
    onSuccess: async ({ deletedChecks }) => {
      setConfirmReset(false)
      setOpenSuite(null)
      await queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormCheckSuites })
      toast(`${deletedChecks} dorm check${deletedChecks === 1 ? '' : 's'} reset`, 'success')
    },
  })

  return <div className="page dorm-check-page">
    <header className="dorm-check-heading">
      <div><span className="eyebrow">Eltse Hall</span><h1>Dorm checks</h1><p>Select a room, complete the checklist, and submit it under your name.</p></div>
      <div className="dorm-check-heading__actions"><div className="dorm-check-heading__buttons"><button type="button" className="button button--danger-quiet dorm-reset" onClick={() => setConfirmReset(true)} disabled={!complete || reset.isPending}><RotateCcw size={17} />Reset checks</button><button type="button" className="button button--quiet dorm-export" onClick={exportPdf} disabled={exporting}><Download size={17} />{exporting ? 'Preparing…' : 'Export PDF'}</button></div><div className="dorm-check-progress" aria-label={`${complete} of ${rooms.length || 100} rooms checked`}><strong>{complete}</strong><span>of {rooms.length || 100}<small>checked</small></span></div></div>
    </header>
    {suites.isLoading && <SuiteSkeleton />}
    {suites.isError && <ErrorState title="Dorm checks unavailable" message={suites.error instanceof ApiError ? suites.error.problem.title : 'The suite list could not be loaded.'} onRetry={() => suites.refetch()} />}
    {suites.data && <div className="suite-grid">{suites.data.map((suite) => <SuiteCard suite={suite} expanded={openSuite === suite.suiteNumber} onToggle={() => setOpenSuite(openSuite === suite.suiteNumber ? null : suite.suiteNumber)} onSelect={setSelectedRoom} key={suite.suiteNumber} />)}</div>}
    {selectedRoom && <RoomCheckDialog room={selectedRoom} onClose={() => setSelectedRoom(null)} />}
    <Dialog open={confirmReset} onClose={() => !reset.isPending && setConfirmReset(false)} title="Reset all dorm checks?" className="reset-checks-dialog"><div className="reset-checks-warning"><AlertTriangle /><div><strong>Are you sure?</strong><p>This permanently removes all {complete} completed dorm check{complete === 1 ? '' : 's'} and attached pictures for Eltse Hall. The suite and resident list will stay in place.</p></div></div><p className="reset-checks-final">This action cannot be undone.</p>{reset.isError && <p className="inline-error" role="alert">{reset.error instanceof ApiError ? reset.error.problem.title : 'The dorm checks could not be reset.'}</p>}<div className="dialog-actions"><button type="button" className="button button--danger" onClick={() => reset.mutate()} disabled={reset.isPending}>{reset.isPending ? 'Resetting…' : 'Yes, reset all checks'}</button><button type="button" className="button button--quiet" onClick={() => setConfirmReset(false)} disabled={reset.isPending}>Cancel</button></div></Dialog>
  </div>
}

function SuiteCard({ suite, expanded, onToggle, onSelect }: { suite: DormSuite; expanded: boolean; onToggle: () => void; onSelect: (room: DormRoom) => void }) {
  const complete = suite.rooms.filter((room) => room.latestCheck).length
  return <section className={`suite-card ${expanded ? 'is-open' : ''}`} aria-labelledby={`suite-${suite.suiteNumber}`}>
    <button type="button" className="suite-toggle" onClick={onToggle} aria-expanded={expanded} aria-controls={`suite-rooms-${suite.suiteNumber}`}><div><span>Suite</span><h2 id={`suite-${suite.suiteNumber}`}>{suite.suiteNumber}</h2></div><span className={complete === 4 ? 'suite-complete is-complete' : 'suite-complete'}>{complete}/4</span><ChevronDown className="suite-chevron" size={19} /></button>
    {expanded && <div className="suite-rooms" id={`suite-rooms-${suite.suiteNumber}`}>{suite.rooms.map((room) => <button type="button" className={`room-row ${room.latestCheck ? 'is-checked' : ''}`} onClick={() => onSelect(room)} key={room.id}>
      <span className="room-letter">{room.latestCheck ? <Check size={17} /> : room.roomLetter}</span>
      <span className="room-people"><strong>Room {room.roomLetter}</strong>{room.residents.length ? room.residents.map((resident) => <small key={resident.id}>{resident.firstName} {resident.lastName}</small>) : <small>Vacant</small>}{room.latestCheck && <em><UserRound size={12} />Checked by {room.latestCheck.checkedByName}</em>}{room.latestCheck && room.latestCheck.photoCount > 0 && <em className="room-photo-count"><Camera size={12} />{room.latestCheck.photoCount} photo{room.latestCheck.photoCount === 1 ? '' : 's'}</em>}</span>
      <ChevronRight size={17} />
    </button>)}</div>}
  </section>
}

const binaryQuestions = [
  ['isRoomClean', 'Is the room clean?'],
  ['isAllFurniturePresent', 'Is all furniture present?'],
  ['isSmokeDetectorClear', 'Is the smoke detector clear?'],
  ['isRoomOdorFree', 'Is the room odor-free?'],
  ['isRoomTrashFree', 'Is the room trash-free?'],
  ['isRoomAlcoholFree', 'Is the room alcohol-free?'],
  ['isRoomDamageFree', 'Is the room free of damage?'],
] as const

type BinaryKey = typeof binaryQuestions[number][0]
type Answer = '' | 'yes' | 'no'
type FormState = Record<BinaryKey, Answer> & { isCommonAreaClean: Answer | 'na'; notes: string }
const initialForm: FormState = {
  isRoomClean: '', isAllFurniturePresent: '', isSmokeDetectorClear: '', isRoomOdorFree: '',
  isRoomTrashFree: '', isRoomAlcoholFree: '', isRoomDamageFree: '', isCommonAreaClean: '', notes: ''
}

function RoomCheckDialog({ room, onClose }: { room: DormRoom; onClose: () => void }) {
  const [form, setForm] = useState<FormState>(initialForm)
  const [photos, setPhotos] = useState<File[]>([])
  const [photoError, setPhotoError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const toast = useToast()
  const submit = useMutation({
    mutationFn: async () => {
      const check = await api<DormRoomCheck>(`/api/dorm-checks/rooms/${room.id}`, { method: 'POST', body: JSON.stringify({
        ...Object.fromEntries(binaryQuestions.map(([key]) => [key, form[key] === 'yes'])),
        isCommonAreaClean: form.isCommonAreaClean === 'na' ? null : form.isCommonAreaClean === 'yes',
        notes: form.notes.trim() || null,
      }) })
      let uploadError: Error | null = null
      if (photos.length) {
        const body = new FormData()
        photos.forEach((photo) => body.append('photosToAdd', photo))
        try { await api<DormCheckPhoto[]>(`/api/dorm-checks/checks/${check.id}/photos`, { method: 'POST', body }) }
        catch (error) { uploadError = error instanceof Error ? error : new Error('Picture upload failed.') }
      }
      return { check, uploadError }
    },
    onSuccess: async ({ uploadError }) => {
      await queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormCheckSuites })
      toast(uploadError ? `${room.roomCode} check saved, but its pictures could not be uploaded.` : `${room.roomCode} check saved`, uploadError ? 'error' : 'success')
      onClose()
    },
  })
  function addPhotos(event: ChangeEvent<HTMLInputElement>) {
    const selected = Array.from(event.target.files ?? [])
    event.target.value = ''
    const supported = new Set(['image/jpeg', 'image/png', 'image/webp', 'image/heic', 'image/heif'])
    const invalidType = selected.find((photo) => !supported.has(photo.type.toLowerCase()))
    const tooLarge = selected.find((photo) => photo.size > 5 * 1024 * 1024)
    if (invalidType) { setPhotoError('Use JPEG, PNG, WebP, HEIC, or HEIF pictures.'); return }
    if (tooLarge) { setPhotoError('Each picture must be 5 MB or smaller.'); return }
    if (photos.length + selected.length > 4) { setPhotoError('You can attach up to 4 pictures to one room check.'); return }
    setPhotoError(null)
    setPhotos((current) => [...current, ...selected])
  }
  function handleSubmit(event: FormEvent) { event.preventDefault(); submit.mutate() }
  return <Dialog open onClose={onClose} title={`${room.roomCode} check`} className="dorm-check-dialog">
    <div className="dorm-dialog-residents"><ClipboardCheck /><div><span>Residents</span>{room.residents.length ? room.residents.map((resident) => <strong key={resident.id}>{resident.firstName} {resident.lastName}</strong>) : <strong>Vacant room</strong>}</div></div>
    <form className="room-check-form" onSubmit={handleSubmit}>
      {binaryQuestions.slice(0, 5).map(([key, question]) => <Question key={key} id={key} question={question} value={form[key]} onChange={(value) => setForm({ ...form, [key]: value })} />)}
      <Question id="isCommonAreaClean" question="Is the common area clean?" value={form.isCommonAreaClean} onChange={(value) => setForm({ ...form, isCommonAreaClean: value })} allowNA />
      {binaryQuestions.slice(5).map(([key, question]) => <Question key={key} id={key} question={question} value={form[key]} onChange={(value) => setForm({ ...form, [key]: value })} />)}
      <section className="room-photos" aria-labelledby="room-photos-title"><div className="room-photos__heading"><div><strong id="room-photos-title">Room pictures</strong><small>Optional · Up to 4 pictures, 5 MB each</small></div><span>{photos.length}/4</span></div><div className="photo-actions"><label className="photo-action"><ImagePlus size={17} /><span>Upload pictures</span><input className="sr-only" type="file" accept="image/jpeg,image/png,image/webp,image/heic,image/heif" multiple onChange={addPhotos} /></label><label className="photo-action"><Camera size={17} /><span>Take picture</span><input className="sr-only" type="file" accept="image/*" capture="environment" onChange={addPhotos} /></label></div>{photos.length > 0 && <div className="selected-photos">{photos.map((photo, index) => <SelectedPhoto file={photo} onRemove={() => setPhotos((current) => current.filter((_, photoIndex) => photoIndex !== index))} key={`${photo.name}-${photo.lastModified}-${index}`} />)}</div>}{photoError && <p className="field-error" role="alert">{photoError}</p>}</section>
      <label className="check-notes"><span>Notes</span><textarea value={form.notes} onChange={(event) => setForm({ ...form, notes: event.target.value })} maxLength={2000} rows={4} placeholder="Add damage details or anything the team should know." /></label>
      {submit.isError && <p className="inline-error" role="alert">{submit.error instanceof ApiError ? submit.error.problem.title : 'The room check could not be saved.'}</p>}
      <div className="dialog-actions"><button className="button button--primary" type="submit" disabled={submit.isPending}>{submit.isPending ? 'Saving…' : 'Complete room check'}</button><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button></div>
    </form>
  </Dialog>
}

function SelectedPhoto({ file, onRemove }: { file: File; onRemove: () => void }) {
  const [url, setUrl] = useState('')
  useEffect(() => {
    const nextUrl = URL.createObjectURL(file)
    setUrl(nextUrl)
    return () => URL.revokeObjectURL(nextUrl)
  }, [file])
  return <figure className="selected-photo">{url && <img src={url} alt={file.name} />}<figcaption>{file.name}</figcaption><button type="button" onClick={onRemove} aria-label={`Remove ${file.name}`}><Trash2 size={14} /></button></figure>
}

function Question({ id, question, value, onChange, allowNA = false }: { id: string; question: string; value: string; onChange: (value: Answer | 'na') => void; allowNA?: boolean }) {
  const answers: Array<Answer | 'na'> = allowNA ? ['yes', 'no', 'na'] : ['yes', 'no']
  return <fieldset className="check-question"><legend>{question}</legend><div className="answer-options">
    {answers.map((answer) => <label className={value === answer ? 'is-selected' : ''} key={answer}><input type="radio" name={id} value={answer} checked={value === answer} onChange={() => onChange(answer)} required /><span>{answer === 'na' ? 'N/A' : answer[0].toUpperCase() + answer.slice(1)}</span></label>)}
  </div></fieldset>
}

function SuiteSkeleton() { return <div className="suite-grid" aria-label="Loading dorm suites">{Array.from({ length: 6 }, (_, index) => <div className="suite-card suite-card--loading" key={index}><div className="skeleton skeleton--title" />{Array.from({ length: 4 }, (__, room) => <div className="skeleton skeleton--cell" key={room} />)}</div>)}</div> }
