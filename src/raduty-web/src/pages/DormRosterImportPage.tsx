import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowRight, CheckCircle2, FileSpreadsheet, Upload } from 'lucide-react'
import { useState, type ChangeEvent } from 'react'
import { ApiError, api } from '../api'
import { Dialog } from '../components/ui'
import { useToast } from '../components/toast'
import type { DormRosterChange, DormRosterImportPreview } from '../types'

export function DormRosterImportPage() {
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [preview, setPreview] = useState<DormRosterImportPreview | null>(null)
  const [confirmApply, setConfirmApply] = useState(false)
  const [applied, setApplied] = useState(false)
  const queryClient = useQueryClient()
  const toast = useToast()
  const analyze = useMutation({
    mutationFn: (selected: File) => api<DormRosterImportPreview>('/api/admin/dorm-roster/preview', { method: 'POST', body: workbookForm(selected) }),
    onSuccess: (result) => { setPreview(result); setApplied(false) },
  })
  const apply = useMutation({
    mutationFn: (selected: File) => api<DormRosterImportPreview>('/api/admin/dorm-roster/apply', { method: 'POST', body: workbookForm(selected) }),
    onSuccess: async (result) => {
      setPreview(result)
      setConfirmApply(false)
      setApplied(true)
      toast(`Resident roster updated with ${result.residentCount} ELTS residents.`)
      await queryClient.invalidateQueries({ queryKey: ['dorm-check-suites'] })
    },
  })
  const changeCount = preview ? preview.addedResidents + preview.removedResidents + preview.movedResidents + preview.updatedResidents : 0

  function selectFile(event: ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0] ?? null
    event.target.value = ''
    setPreview(null)
    setApplied(false)
    if (!selected) return
    if (!selected.name.toLowerCase().endsWith('.xlsx')) { setFile(null); setFileError('Choose an Excel .xlsx spreadsheet.'); return }
    if (selected.size > 10 * 1024 * 1024) { setFile(null); setFileError('The spreadsheet must be 10 MB or smaller.'); return }
    setFileError(null)
    setFile(selected)
  }

  return <div className="page roster-import-page">
    <header className="roster-import-heading"><div><span className="eyebrow">Hall Director</span><h1>Resident roster</h1><p>Upload the latest housing spreadsheet, review room changes, then update Eltse Hall.</p></div></header>

    <section className="roster-upload-card" aria-labelledby="upload-roster-title">
      <div className="roster-upload-copy"><span className="roster-upload-icon"><FileSpreadsheet /></span><div><h2 id="upload-roster-title">Upload housing spreadsheet</h2><p>Required columns: <strong>LastName</strong>, <strong>FirstName</strong>, and <strong>Room</strong>. Sport/Activity is optional.</p></div></div>
      <label className="roster-file-picker"><Upload size={18} /><span>{file ? 'Choose a different file' : 'Choose Excel file'}</span><input className="sr-only" type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={selectFile} aria-label="Excel spreadsheet" /></label>
      {file && <div className="roster-selected-file"><FileSpreadsheet size={19} /><span><strong>{file.name}</strong><small>{formatBytes(file.size)}</small></span><button className="button button--primary" type="button" onClick={() => analyze.mutate(file)} disabled={analyze.isPending}>{analyze.isPending ? 'Analyzing…' : 'Analyze spreadsheet'}<ArrowRight size={16} /></button></div>}
      {fileError && <p className="inline-error" role="alert">{fileError}</p>}
      {analyze.isError && <p className="inline-error" role="alert">{errorTitle(analyze.error, 'The spreadsheet could not be analyzed.')}</p>}
    </section>

    {preview && <section className="roster-analysis" aria-labelledby="roster-analysis-title">
      <div className="roster-analysis-heading"><div><span className="eyebrow">Preview</span><h2 id="roster-analysis-title">Roster analysis</h2><p>No resident data changes until you apply this preview.</p></div>{applied && <span className="roster-applied"><CheckCircle2 size={17} />Applied</span>}</div>
      <div className="roster-metrics"><Metric value={preview.residentCount} label="ELTS residents" /><Metric value={preview.occupiedRooms} label="Occupied rooms" /><Metric value={changeCount} label="Changes found" tone={changeCount ? 'warning' : 'good'} /><Metric value={preview.ignoredRows} label="Other-hall rows ignored" /></div>

      {preview.issues.length > 0 && <div className="roster-issues" role="alert"><AlertTriangle /><div><strong>Fix these spreadsheet issues</strong><ul>{preview.issues.map((issue, index) => <li key={`${issue.rowNumber ?? 'file'}-${index}`}>{issue.rowNumber ? `Row ${issue.rowNumber}: ` : ''}{issue.message}</li>)}</ul></div></div>}
      {preview.canApply && changeCount === 0 && <div className="roster-match"><CheckCircle2 /><div><strong>The roster already matches</strong><p>No room or resident changes were found.</p></div></div>}
      {preview.changes.length > 0 && <div className="roster-change-list responsive-table"><div className="roster-change-summary"><strong>{changeCount} proposed change{changeCount === 1 ? '' : 's'}</strong><span>{preview.movedResidents} moved · {preview.addedResidents} added · {preview.removedResidents} removed · {preview.updatedResidents} updated</span></div><table><thead><tr><th>Resident</th><th>Change</th><th>From</th><th>To</th></tr></thead><tbody>{preview.changes.map((change, index) => <ChangeRow change={change} key={`${change.type}-${change.firstName}-${change.lastName}-${index}`} />)}</tbody></table></div>}
      {preview.canApply && changeCount > 0 && !applied && <div className="roster-apply-bar"><div><strong>Ready to update Eltse Hall</strong><span>Dorm checks stay attached to their rooms.</span></div><button type="button" className="button button--primary" onClick={() => setConfirmApply(true)}>Apply roster changes</button></div>}
    </section>}

    <Dialog open={confirmApply} onClose={() => !apply.isPending && setConfirmApply(false)} title="Apply resident roster?" className="roster-confirm-dialog"><div className="roster-confirm-copy"><AlertTriangle /><div><strong>Update all Eltse Hall room assignments?</strong><p>This will apply {changeCount} resident change{changeCount === 1 ? '' : 's'} from <b>{file?.name}</b>. Existing dorm-check responses will remain attached to their rooms.</p></div></div>{apply.isError && <p className="inline-error" role="alert">{errorTitle(apply.error, 'The resident roster could not be updated.')}</p>}<div className="dialog-actions"><button type="button" className="button button--primary" onClick={() => file && apply.mutate(file)} disabled={!file || apply.isPending}>{apply.isPending ? 'Updating roster…' : 'Yes, apply changes'}</button><button type="button" className="button button--quiet" onClick={() => setConfirmApply(false)} disabled={apply.isPending}>Cancel</button></div></Dialog>
  </div>
}

function Metric({ value, label, tone = '' }: { value: number; label: string; tone?: string }) { return <div className={`roster-metric ${tone ? `roster-metric--${tone}` : ''}`}><strong>{value}</strong><span>{label}</span></div> }

function ChangeRow({ change }: { change: DormRosterChange }) {
  return <tr><td data-label="Resident"><strong>{change.firstName} {change.lastName}</strong></td><td data-label="Change"><span className={`roster-change roster-change--${change.type.toLowerCase()}`}>{change.type}</span></td><td data-label="From">{change.fromRoom ?? '—'}</td><td data-label="To">{change.toRoom ?? '—'}</td></tr>
}

function workbookForm(file: File) { const body = new FormData(); body.append('workbook', file); return body }
function formatBytes(bytes: number) { return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB` }
function errorTitle(error: Error, fallback: string) { return error instanceof ApiError ? error.problem.title : fallback }
