import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, CheckCircle2, ChevronRight, ClipboardList, Download, Sparkles } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { api, ApiError, downloadPdf } from '../api'
import { Dialog, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'
import { residentQueryKeys, sharedResidentQueryOptions } from '../residentData'
import type { DormSuiteSweep, DormSweepSuite } from '../types'

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
  const [exporting, setExporting] = useState(false)
  const toast = useToast()
  const completed = suites.data?.filter((suite) => suite.latestSweep).length ?? 0
  const concerns = suites.data?.filter((suite) => suite.latestSweep?.hasConcerns).length ?? 0
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
        <button type="button" className="button button--quiet dorm-export" onClick={exportPdf} disabled={exporting}><Download size={17} />{exporting ? 'Preparing...' : 'Export PDF'}</button>
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
  const [form, setForm] = useState<SweepForm>(initialForm)
  const queryClient = useQueryClient()
  const toast = useToast()
  const submit = useMutation({
    mutationFn: () => api<DormSuiteSweep>(`/api/dorm-sweeps/suites/${suite.suiteNumber}`, {
      method: 'POST',
      body: JSON.stringify({ ...form, notes: form.notes.trim() || null }),
    }),
    onSuccess: async (sweep) => {
      await queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormSweepSuites })
      toast(`Suite ${sweep.suiteNumber} sweep saved`, 'success')
      onClose()
    },
  })
  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    submit.mutate()
  }
  return <Dialog open onClose={onClose} title={`Suite ${suite.suiteNumber} sweep`} className="dorm-check-dialog">
    <div className="sweep-dialog-intro"><Sparkles /><div><strong>Bathrooms and common area</strong><span>Review the suite as one shared space.</span></div></div>
    <form className="room-check-form" onSubmit={handleSubmit}>
      {sweepQuestions.map(([key, question]) => <SweepQuestion key={key} id={key} question={question} value={form[key]} onChange={(value) => setForm({ ...form, [key]: value })} />)}
      <label className="check-notes"><span>Notes</span><textarea value={form.notes} onChange={(event) => setForm({ ...form, notes: event.target.value })} maxLength={2000} rows={4} placeholder="Add trash, bathroom, damage, or follow-up details." /></label>
      {submit.isError && <p className="inline-error" role="alert">{submit.error instanceof ApiError ? submit.error.problem.title : 'The suite sweep could not be saved.'}</p>}
      <div className="dialog-actions"><button className="button button--primary" type="submit" disabled={submit.isPending}>{submit.isPending ? 'Saving...' : 'Save sweep'}</button><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button></div>
    </form>
  </Dialog>
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
