import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, AlertTriangle, Camera, CheckCircle2, ChevronRight, ClipboardList, Download, ImagePlus, RotateCcw, Sparkles, Trash2 } from 'lucide-react'
import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { api, ApiError, downloadPdf } from '../api'
import { Dialog, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'
import { residentQueryKeys, sharedResidentQueryOptions } from '../residentData'
import type { DormSuiteSweep, DormSweepPhoto, DormSweepReset, DormSweepSuite } from '../types'

const sweepQuestions = [
  ['isCommonAreaClean', 'Is the common area clean?'],
  ['hasMoldOrLeak', 'Is there any mold or leaking in the common area/bathroom?'],
  ['hasFurnitureMovedToCommonArea', 'Is there any furniture that has been moved to the common area?'],
  ['isBathroomClean', 'Is the bathroom clean?'],
  ['areToiletsAndSinksWorking', 'Are the bathroom toilet and sinks working?'],
  ['areShowersWorking', 'Are the showers working?'],
  ['smellsLikeMarijuanaOrAlcohol', 'Does the suite smell like marijuana or alcohol?'],
] as const

type SweepKey = typeof sweepQuestions[number][0]
type SweepForm = Record<SweepKey, boolean> & { notes: string }
const initialForm: SweepForm = {
  isCommonAreaClean: true,
  hasMoldOrLeak: false,
  hasFurnitureMovedToCommonArea: false,
  isBathroomClean: true,
  areToiletsAndSinksWorking: true,
  areShowersWorking: true,
  smellsLikeMarijuanaOrAlcohol: false,
  notes: '',
}

export function DormSweepPage() {
  const suites = useQuery({ queryKey: residentQueryKeys.dormSweepSuites, queryFn: ({ signal }) => api<DormSweepSuite[]>('/api/dorm-sweeps/suites', {}, signal), ...sharedResidentQueryOptions })
  const [selectedSuite, setSelectedSuite] = useState<DormSweepSuite | null>(null)
  const [confirmReset, setConfirmReset] = useState(false)
  const [exporting, setExporting] = useState(false)
  const queryClient = useQueryClient()
  const toast = useToast()
  const completed = suites.data?.filter((suite) => suite.latestSweep).length ?? 0
  const concerns = suites.data?.filter((suite) => suite.latestSweep?.hasConcerns).length ?? 0
  const reset = useMutation({
    mutationFn: () => api<DormSweepReset>('/api/dorm-sweeps', { method: 'DELETE' }),
    onSuccess: async ({ deletedSweeps }) => {
      setConfirmReset(false)
      setSelectedSuite(null)
      await queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormSweepSuites })
      toast(`${deletedSweeps} dorm sweep${deletedSweeps === 1 ? '' : 's'} reset`, 'success')
    },
  })
  async function exportPdf() {
    setExporting(true)
    try { await downloadPdf('/api/dorm-sweeps/pdf', 'eltse-hall-dorm-sweeps.pdf') }
    catch (error) { toast(error instanceof ApiError ? error.problem.title : 'The PDF could not be downloaded.', 'error') }
    finally { setExporting(false) }
  }

  return <div className="page dorm-sweep-page">
    <header className="dorm-check-heading">
      <div><span className="eyebrow">Eltse Hall</span><h1>Dorm sweeps</h1><p>Check suite common areas and bathrooms without changing the room-check records.</p></div>
      <div className="dorm-check-heading__actions">
        <div className="dorm-check-heading__buttons"><button type="button" className="button button--danger-quiet dorm-reset" onClick={() => setConfirmReset(true)} disabled={reset.isPending}><RotateCcw size={17} />Reset sweeps</button><button type="button" className="button button--quiet dorm-export" onClick={exportPdf} disabled={exporting}><Download size={17} />{exporting ? 'Preparing...' : 'Export PDF'}</button></div>
        <div className="sweep-summary" aria-label={`${completed} suites swept, ${concerns} need attention`}>
          <span><strong>{completed}</strong><small>swept</small></span>
          <span className={concerns ? 'has-concerns' : ''}><strong>{concerns}</strong><small>attention</small></span>
        </div>
      </div>
    </header>
    {suites.isLoading && <SweepSkeleton />}
    {suites.isError && <ErrorState title="Dorm sweeps unavailable" message={suites.error instanceof ApiError ? suites.error.problem.title : 'The suite list could not be loaded.'} onRetry={() => suites.refetch()} />}
    {suites.data && <div className="sweep-grid">{suites.data.map((suite) => <SweepSuiteCard suite={suite} onSelect={setSelectedSuite} key={suite.suiteNumber} />)}</div>}
    {selectedSuite && <SweepDialog suite={selectedSuite} onClose={() => setSelectedSuite(null)} />}
    <Dialog open={confirmReset} onClose={() => !reset.isPending && setConfirmReset(false)} title="Reset all dorm sweeps?" className="reset-checks-dialog"><div className="reset-checks-warning"><AlertTriangle /><div><strong>Are you sure?</strong><p>This permanently deletes all saved dorm sweep records for Eltse Hall. The suite and resident lists will stay in place.</p></div></div><p className="reset-checks-final">This action cannot be undone.</p>{reset.isError && <p className="inline-error" role="alert">{reset.error instanceof ApiError ? reset.error.problem.title : 'The dorm sweeps could not be reset.'}</p>}<div className="dialog-actions"><button type="button" className="button button--danger" onClick={() => reset.mutate()} disabled={reset.isPending}>{reset.isPending ? 'Resetting...' : 'Yes, reset all sweeps'}</button><button type="button" className="button button--quiet" onClick={() => setConfirmReset(false)} disabled={reset.isPending}>Cancel</button></div></Dialog>
  </div>
}

function SweepSuiteCard({ suite, onSelect }: { suite: DormSweepSuite; onSelect: (suite: DormSweepSuite) => void }) {
  const latest = suite.latestSweep
  const checkedAt = latest ? new Date(latest.checkedAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) : null
  return <button type="button" className={`sweep-suite ${latest ? 'is-complete' : ''} ${latest?.hasConcerns ? 'has-concerns' : ''}`} onClick={() => onSelect(suite)}>
    <span className="sweep-suite__number">Suite {suite.suiteNumber}</span>
    <span className="sweep-suite__status">{!latest ? <><ClipboardList size={17} />Not swept</> : latest.hasConcerns ? <><AlertCircle size={17} />Needs attention</> : <><CheckCircle2 size={17} />Clear</>}</span>
    {latest && <span className="sweep-suite__meta">By {latest.checkedByName}<small>{checkedAt}</small></span>}
    <ChevronRight className="sweep-suite__arrow" size={18} />
  </button>
}

function SweepDialog({ suite, onClose }: { suite: DormSweepSuite; onClose: () => void }) {
  const [form, setForm] = useState<SweepForm>(() => formFromSavedSweep(suite.latestSweepDetails))
  const [photos, setPhotos] = useState<File[]>([])
  const [photoError, setPhotoError] = useState<string | null>(null)
  const savedPhotos = suite.latestSweepDetails?.photos ?? []
  const queryClient = useQueryClient()
  const toast = useToast()
  const submit = useMutation({
    mutationFn: async () => {
      const sweep = await api<DormSuiteSweep>(`/api/dorm-sweeps/suites/${suite.suiteNumber}`, {
        method: 'POST', body: JSON.stringify({ ...form, notes: form.notes.trim() || null }),
      })
      let uploadError: Error | null = null
      if (photos.length) {
        const body = new FormData()
        photos.forEach((photo) => body.append('photosToAdd', photo))
        try { await api<DormSweepPhoto[]>(`/api/dorm-sweeps/sweeps/${sweep.id}/photos`, { method: 'POST', body }) }
        catch (error) { uploadError = error instanceof Error ? error : new Error('Picture upload failed.') }
      }
      return { sweep, uploadError }
    },
    onSuccess: async ({ sweep, uploadError }) => {
      await queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormSweepSuites })
      toast(uploadError ? `Suite ${sweep.suiteNumber} sweep saved, but its pictures could not be uploaded.` : `Suite ${sweep.suiteNumber} sweep saved`, uploadError ? 'error' : 'success')
      onClose()
    },
  })
  function addPhotos(event: ChangeEvent<HTMLInputElement>) {
    const selected = Array.from(event.target.files ?? [])
    event.target.value = ''
    const supported = new Set(['image/jpeg', 'image/png', 'image/webp'])
    const invalidType = selected.find((photo) => !supported.has(photo.type.toLowerCase()))
    const tooLarge = selected.find((photo) => photo.size > 5 * 1024 * 1024)
    if (invalidType) { setPhotoError('Use JPEG, PNG, or WebP pictures so they can appear in the PDF.'); return }
    if (tooLarge) { setPhotoError('Each picture must be 5 MB or smaller.'); return }
    if (savedPhotos.length + photos.length + selected.length > 4) { setPhotoError('You can attach up to 4 pictures to one dorm sweep.'); return }
    setPhotoError(null)
    setPhotos((current) => [...current, ...selected])
  }
  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    submit.mutate()
  }
  return <Dialog open onClose={onClose} title={`Suite ${suite.suiteNumber} sweep`} className="dorm-check-dialog">
    <div className="sweep-dialog-intro"><Sparkles /><div><strong>Bathrooms and common area</strong><span>Review the suite as one shared space.</span></div></div>
    <form className="room-check-form" onSubmit={handleSubmit}>
      {sweepQuestions.map(([key, question]) => <SweepQuestion key={key} id={key} question={question} value={form[key]} onChange={(value) => setForm({ ...form, [key]: value })} />)}
      <section className="room-photos" aria-labelledby="sweep-photos-title"><div className="room-photos__heading"><div><strong id="sweep-photos-title">Suite pictures</strong><small>Optional · Up to 4 pictures, 5 MB each · JPEG, PNG, or WebP</small></div><span>{savedPhotos.length + photos.length}/4</span></div><div className="photo-actions"><label className="photo-action"><ImagePlus size={17} /><span>Upload pictures</span><input className="sr-only" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={addPhotos} /></label><label className="photo-action"><Camera size={17} /><span>Take picture</span><input className="sr-only" type="file" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={addPhotos} /></label></div>{(savedPhotos.length > 0 || photos.length > 0) && <div className="selected-photos">{savedPhotos.map((photo) => <figure className="selected-photo" key={photo.id}><img src={`/api/dorm-sweeps/photos/${photo.id}`} alt={photo.fileName} /><figcaption>{photo.fileName}</figcaption></figure>)}{photos.map((photo, index) => <SelectedPhoto file={photo} onRemove={() => setPhotos((current) => current.filter((_, photoIndex) => photoIndex !== index))} key={`${photo.name}-${photo.lastModified}-${index}`} />)}</div>}{photoError && <p className="field-error" role="alert">{photoError}</p>}</section>
      <label className="check-notes"><span>Notes</span><textarea value={form.notes} onChange={(event) => setForm({ ...form, notes: event.target.value })} maxLength={2000} rows={4} placeholder="Add trash, bathroom, damage, or follow-up details." /></label>
      {submit.isError && <p className="inline-error" role="alert">{submit.error instanceof ApiError ? submit.error.problem.title : 'The suite sweep could not be saved.'}</p>}
      <div className="dialog-actions"><button className="button button--primary" type="submit" disabled={submit.isPending}>{submit.isPending ? 'Saving...' : suite.latestSweepDetails ? 'Update sweep' : 'Save sweep'}</button><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button></div>
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

function formFromSavedSweep(sweep?: DormSuiteSweep | null): SweepForm {
  if (!sweep) return initialForm
  return {
    isCommonAreaClean: sweep.isCommonAreaClean,
    hasMoldOrLeak: sweep.hasMoldOrLeak,
    hasFurnitureMovedToCommonArea: sweep.hasFurnitureMovedToCommonArea,
    isBathroomClean: sweep.isBathroomClean,
    areToiletsAndSinksWorking: sweep.areToiletsAndSinksWorking,
    areShowersWorking: sweep.areShowersWorking,
    smellsLikeMarijuanaOrAlcohol: sweep.smellsLikeMarijuanaOrAlcohol,
    notes: sweep.notes ?? '',
  }
}

function SweepQuestion({ id, question, value, onChange }: { id: string; question: string; value: boolean; onChange: (value: boolean) => void }) {
  return <fieldset className="check-question"><legend>{question}</legend><div className="answer-options">
    <label className={value ? 'is-selected' : ''}><input type="radio" name={id} checked={value} onChange={() => onChange(true)} /><span>Yes</span></label>
    <label className={!value ? 'is-selected' : ''}><input type="radio" name={id} checked={!value} onChange={() => onChange(false)} /><span>No</span></label>
  </div></fieldset>
}

function SweepSkeleton() {
  return <div className="sweep-grid" aria-label="Loading dorm sweep suites">{Array.from({ length: 9 }, (_, index) => <div className="sweep-suite sweep-suite--loading" key={index}><div className="skeleton skeleton--title" /><div className="skeleton skeleton--cell" /></div>)}</div>
}
