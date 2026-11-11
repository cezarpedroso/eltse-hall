import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Download, FilePlus2, Minus, Pencil, Plus, ShieldCheck, Trash2, UsersRound } from 'lucide-react'
import { api, ApiError, downloadPdf } from '../api'
import { Dialog, EmptyState, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'
import type { CurrentUser, IncidentReport, IncidentReportSummary, IncidentType, ResidentAssistant } from '../types'

type StudentDraft = { firstName: string; lastName: string; studentId: string; roomNumber: string; roomLetter: string }
type IncidentForm = { incidentType: IncidentType; occurredAt: string; location: string; description: string; alcoholOunces: string; infractionPrice: string; staffPresentUserIds: string[]; students: StudentDraft[] }

const incidentTypes: Array<[IncidentType, string]> = [
  ['Alcohol', 'Alcohol found'], ['Smoking', 'Smoking'], ['SmokeDetectorCovered', 'Smoke detector covered'],
  ['Fight', 'Fight'], ['IllegalAppliance', 'Illegal appliance'], ['LoudNoise', 'Loud noise'],
  ['UnauthorizedRoomMove', 'Unauthorized room move'], ['Assault', 'Assault'], ['Other', 'Other'],
]

function emptyStudent(): StudentDraft { return { firstName: '', lastName: '', studentId: '', roomNumber: '', roomLetter: '' } }
function initialForm(): IncidentForm { return { incidentType: 'Alcohol', occurredAt: localDateTime(), location: '', description: '', alcoholOunces: '', infractionPrice: '', staffPresentUserIds: [], students: [emptyStudent()] } }
function localDateTime() {
  const date = new Date()
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function typeLabel(type: IncidentType) { return incidentTypes.find(([value]) => value === type)?.[1] ?? type }

export function IncidentReportsPage({ user }: { user: CurrentUser }) {
  const [showForm, setShowForm] = useState(false)
  const [editingReportId, setEditingReportId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<IncidentReportSummary | null>(null)
  const [form, setForm] = useState<IncidentForm>(initialForm)
  const queryClient = useQueryClient()
  const toast = useToast()
  const reports = useQuery({ queryKey: ['incident-reports'], queryFn: ({ signal }) => api<IncidentReportSummary[]>('/api/incidents', {}, signal) })
  const directory = useQuery({ queryKey: ['incident-staff'], queryFn: ({ signal }) => api<ResidentAssistant[]>('/api/resident-assistants', {}, signal), enabled: showForm })
  const create = useMutation({
    mutationFn: () => api<IncidentReport>(editingReportId ? `/api/incidents/${editingReportId}` : '/api/incidents', {
      method: editingReportId ? 'PUT' : 'POST', body: JSON.stringify({
        incidentType: form.incidentType, occurredAt: new Date(form.occurredAt).toISOString(), location: form.location,
        description: form.description, alcoholOunces: form.incidentType === 'Alcohol' && form.alcoholOunces ? Number(form.alcoholOunces) : null,
        infractionPrice: form.infractionPrice ? Number(form.infractionPrice) : null, staffPresentUserIds: form.staffPresentUserIds,
        students: form.students,
      }),
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['incident-reports'] })
      setForm(initialForm()); setShowForm(false); setEditingReportId(null); toast(editingReportId ? 'Incident report updated.' : 'Incident report saved. You can download its PDF from the report list.')
    },
  })
  const remove = useMutation({ mutationFn: (id: string) => api<void>(`/api/incidents/${id}`, { method: 'DELETE' }), onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['incident-reports'] }); setDeleteTarget(null); toast('Incident report deleted.') } })
  function beginReport() { setForm(initialForm()); setEditingReportId(null); setShowForm(true) }
  async function beginEdit(id: string) {
    try {
      const report = await api<IncidentReport>(`/api/incidents/${id}`)
      setForm({ incidentType: report.incidentType, occurredAt: localDateTimeFromIso(report.occurredAt), location: report.location, description: report.description, alcoholOunces: report.alcoholOunces?.toString() ?? '', infractionPrice: report.infractionPrice?.toString() ?? '', staffPresentUserIds: report.staffPresent.map((staff) => staff.id), students: report.students.map((student) => ({ firstName: student.firstName, lastName: student.lastName, studentId: student.studentId, roomNumber: student.roomNumber, roomLetter: student.roomLetter })) })
      setEditingReportId(id); setShowForm(true)
    } catch (error) { toast(error instanceof ApiError ? error.problem.title : 'The incident report could not be opened.', 'error') }
  }
  function updateStudent(index: number, field: keyof StudentDraft, value: string) {
    setForm((current) => ({ ...current, students: current.students.map((student, studentIndex) => studentIndex === index ? { ...student, [field]: value } : student) }))
  }
  function removeStudent(index: number) { setForm((current) => ({ ...current, students: current.students.filter((_, studentIndex) => studentIndex !== index) })) }
  function addStaff(userId: string) { if (userId) setForm((current) => current.staffPresentUserIds.includes(userId) ? current : { ...current, staffPresentUserIds: [...current.staffPresentUserIds, userId] }) }
  function submit(event: FormEvent) { event.preventDefault(); create.mutate() }
  const canManageReports = user.role === 'HallDirector' || user.role === 'Admin' || user.role === 'DeanOfStudents' || user.role === 'AssistantDirectorOfResidenceLife'
  const staff = (directory.data ?? []).filter((member) => member.id !== user.id)
  const selectedStaff = staff.filter((member) => form.staffPresentUserIds.includes(member.id))
  return <div className="page page--form incident-page">
    <header className="page-heading incident-heading"><div><span className="eyebrow">Residence life records</span><h1>Incident reports</h1><p>Document what happened, who was involved, and any applicable infraction price.</p></div><button className="button button--primary" type="button" onClick={beginReport}><FilePlus2 size={17} />New incident report</button></header>

    {showForm && <section className="incident-form-panel" aria-labelledby="incident-form-title">
      <div className="incident-form-panel__header"><div><span className="eyebrow">{editingReportId ? 'Edit report' : 'New report'}</span><h2 id="incident-form-title">Incident details</h2><p>Fields marked <span className="required-field" aria-hidden="true">*</span> are required.</p></div><button className="text-button" type="button" onClick={() => { setShowForm(false); setEditingReportId(null) }}>Cancel</button></div>
      <form className="incident-form" onSubmit={submit}>
        <div className="form-grid"><label className="field"><span>Incident type <i className="required-field" aria-hidden="true">*</i></span><select value={form.incidentType} onChange={(event) => setForm({ ...form, incidentType: event.target.value as IncidentType })}>{incidentTypes.map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label><label className="field"><span>Date and time it happened <i className="required-field" aria-hidden="true">*</i></span><input type="datetime-local" value={form.occurredAt} onChange={(event) => setForm({ ...form, occurredAt: event.target.value })} required /></label><label className="field field--wide"><span>Location <i className="required-field" aria-hidden="true">*</i></span><input value={form.location} onChange={(event) => setForm({ ...form, location: event.target.value })} maxLength={160} placeholder="Example: ELTS-12B, east hallway, main lobby" required /></label></div>
        {form.incidentType === 'Assault' && <div className="incident-safety" role="note"><AlertTriangle size={20} /><div><strong>Immediate safety first</strong><span>For an active emergency or immediate danger, contact campus safety or 911 before completing this report.</span></div></div>}
        {form.incidentType === 'Alcohol' && <label className="field incident-amount"><span>Alcohol found (ounces) <i className="required-field" aria-hidden="true">*</i></span><input type="number" inputMode="decimal" min="0.01" max="10000" step="0.01" value={form.alcoholOunces} onChange={(event) => setForm({ ...form, alcoholOunces: event.target.value })} placeholder="Example: 12" required /></label>}
        <label className="field"><span>Infraction price <small>Optional</small></span><div className="currency-input"><span>$</span><input type="number" inputMode="decimal" min="0" max="1000000" step="0.01" value={form.infractionPrice} onChange={(event) => setForm({ ...form, infractionPrice: event.target.value })} placeholder="0.00" /></div></label>
        <section className="incident-staff" aria-labelledby="incident-staff-title"><div className="incident-section-heading"><div><h2 id="incident-staff-title">Staff</h2><p><strong>Reporter:</strong> {user.firstName} {user.lastName} (saved automatically)</p></div></div><label className="field"><span>Other RAs or staff present <small>Optional</small></span><select value="" onChange={(event) => addStaff(event.target.value)} disabled={directory.isLoading}><option value="">{directory.isLoading ? 'Loading staff…' : 'Select a staff member to add'}</option>{staff.filter((member) => !form.staffPresentUserIds.includes(member.id)).map((member) => <option value={member.id} key={member.id}>{member.firstName} {member.lastName} · {member.role === 'ResidentAssistant' ? 'RA' : member.role === 'HallDirector' ? 'Hall Director' : 'Admin'}</option>)}</select></label>{selectedStaff.length > 0 && <ul className="incident-staff-list">{selectedStaff.map((member) => <li key={member.id}><span>{member.firstName} {member.lastName}</span><button type="button" aria-label={`Remove ${member.firstName} ${member.lastName}`} onClick={() => setForm((current) => ({ ...current, staffPresentUserIds: current.staffPresentUserIds.filter((id) => id !== member.id) }))}><Minus size={15} />Remove</button></li>)}</ul>}</section>
        <section className="incident-students" aria-labelledby="incident-students-title"><div className="incident-section-heading"><div><h2 id="incident-students-title">Students involved <i className="required-field" aria-hidden="true">*</i></h2><p>Add every student connected to this incident.</p></div><button className="button button--quiet" type="button" onClick={() => setForm((current) => ({ ...current, students: [...current.students, emptyStudent()] }))}><Plus size={16} />Add student</button></div>{form.students.map((student, index) => <fieldset className="incident-student" key={index}><legend>Student {index + 1}</legend><div className="incident-student__top">{form.students.length > 1 && <button className="text-button incident-student__remove" type="button" onClick={() => removeStudent(index)}><Minus size={15} />Remove student</button>}</div><div className="form-grid"><label className="field"><span>First name <i className="required-field" aria-hidden="true">*</i></span><input value={student.firstName} onChange={(event) => updateStudent(index, 'firstName', event.target.value)} maxLength={80} required /></label><label className="field"><span>Last name <i className="required-field" aria-hidden="true">*</i></span><input value={student.lastName} onChange={(event) => updateStudent(index, 'lastName', event.target.value)} maxLength={80} required /></label><label className="field"><span>Student ID <i className="required-field" aria-hidden="true">*</i></span><input value={student.studentId} onChange={(event) => updateStudent(index, 'studentId', event.target.value)} maxLength={40} required /></label><label className="field"><span>Room number <i className="required-field" aria-hidden="true">*</i></span><input value={student.roomNumber} onChange={(event) => updateStudent(index, 'roomNumber', event.target.value)} maxLength={10} placeholder="12" required /></label><label className="field"><span>Room letter <i className="required-field" aria-hidden="true">*</i></span><input value={student.roomLetter} onChange={(event) => updateStudent(index, 'roomLetter', event.target.value.toUpperCase())} maxLength={4} placeholder="B" required /></label></div></fieldset>)}</section>
        <label className="field"><span>Description of what happened <i className="required-field" aria-hidden="true">*</i></span><textarea value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} maxLength={4000} rows={7} placeholder="Include the observable facts, actions taken, and relevant follow-up information." required /></label>
        {create.isError && <p className="inline-error" role="alert">{create.error instanceof ApiError ? create.error.problem.title : 'The incident report could not be saved.'}</p>}
        <div className="form-actions"><button className="button button--primary" type="submit" disabled={create.isPending}>{create.isPending ? 'Saving report…' : editingReportId ? 'Save changes' : 'Save incident report'}</button><button className="button button--quiet" type="button" onClick={() => { setShowForm(false); setEditingReportId(null) }} disabled={create.isPending}>Cancel</button></div>
      </form>
    </section>}

    <section className="incident-history" aria-labelledby="incident-history-title"><div className="section-heading"><div><span className="eyebrow">Saved records</span><h2 id="incident-history-title">Report history</h2><p>Download an individual PDF when you need a record of an infraction.</p></div></div>{reports.isLoading && <div className="table-skeleton">{Array.from({ length: 4 }, (_, index) => <div className="skeleton skeleton--row" key={index} />)}</div>}{reports.isError && <ErrorState title="Incident reports unavailable" message={reports.error instanceof ApiError ? reports.error.problem.title : 'The report history could not be loaded.'} onRetry={() => reports.refetch()} />}{reports.data && reports.data.length === 0 && <EmptyState title="No incident reports yet" message="Create a report when a residence-life incident occurs." />}{reports.data && reports.data.length > 0 && <div className="incident-list">{reports.data.map((report) => <IncidentRow report={report} canManage={canManageReports} onEdit={() => beginEdit(report.id)} onDelete={() => setDeleteTarget(report)} onDownload={async () => { try { await downloadPdf(`/api/incidents/${report.id}/pdf`, `incident-${report.occurredAt.slice(0, 10)}.pdf`) } catch (error) { toast(error instanceof ApiError ? error.problem.title : 'The incident PDF could not be downloaded.', 'error') } }} key={report.id} />)}</div>}</section>
    <div className="privacy-callout"><ShieldCheck size={18} /><p><strong>Handle reports carefully.</strong> Incident records contain restricted residence-life information and are available only to authorized staff.</p></div>
    <Dialog open={deleteTarget !== null} onClose={() => !remove.isPending && setDeleteTarget(null)} title="Delete incident report?"><div className="confirm-dialog"><AlertTriangle /><p>This permanently deletes the {deleteTarget && typeLabel(deleteTarget.incidentType).toLowerCase()} incident report. This cannot be undone.</p></div>{remove.isError && <p className="inline-error" role="alert">{remove.error instanceof ApiError ? remove.error.problem.title : 'The incident report could not be deleted.'}</p>}<div className="dialog-actions"><button type="button" className="button button--danger" onClick={() => deleteTarget && remove.mutate(deleteTarget.id)} disabled={remove.isPending}>{remove.isPending ? 'Deleting…' : 'Delete report'}</button><button type="button" className="button button--quiet" onClick={() => setDeleteTarget(null)} disabled={remove.isPending}>Cancel</button></div></Dialog>
  </div>
}

function IncidentRow({ report, canManage, onDownload, onEdit, onDelete }: { report: IncidentReportSummary; canManage: boolean; onDownload: () => void; onEdit: () => void; onDelete: () => void }) {
  return <article className="incident-row"><span className="incident-row__type">{typeLabel(report.incidentType)}</span><div className="incident-row__details"><strong>{report.location}</strong><span>{new Date(report.occurredAt).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })} · Reported by {report.reportedByName}</span></div><div className="incident-row__meta"><span><UsersRound size={15} />{report.studentCount} student{report.studentCount === 1 ? '' : 's'}</span><strong>{report.infractionPrice == null ? 'No price' : `$${report.infractionPrice.toFixed(2)}`}</strong></div><div className="incident-row__actions"><button className="button button--quiet" type="button" onClick={onDownload}><Download size={16} />PDF</button>{canManage && <><button className="button button--quiet" type="button" onClick={onEdit}><Pencil size={16} />Edit</button><button className="button button--danger-quiet" type="button" onClick={onDelete}><Trash2 size={16} />Delete</button></>}</div></article>
}

function localDateTimeFromIso(value: string) { const date = new Date(value); date.setMinutes(date.getMinutes() - date.getTimezoneOffset()); return date.toISOString().slice(0, 16) }
