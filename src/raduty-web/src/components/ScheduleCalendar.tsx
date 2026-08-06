import { useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Check, Clock3, Users } from 'lucide-react'
import { api, ApiError } from '../api'
import type { CurrentUser, Schedule, Shift } from '../types'
import { timeRange, monthName } from '../scheduleFormat'
import { Dialog } from './ui'
import { useToast } from './toast'

const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

export function ScheduleCalendar({ schedule }: { schedule: Schedule; user: CurrentUser }) {
  const [selected, setSelected] = useState<Shift | null>(null)
  const firstWeekday = useMemo(() => schedule.shifts.length ? dateValue(schedule.shifts[0].dutyDate).getDay() : 0, [schedule])
  const calendarItems = [...Array<Shift | null>(firstWeekday).fill(null), ...schedule.shifts]
  while (calendarItems.length % 7) calendarItems.push(null)
  return <>
    <div className="calendar-view is-active simple-calendar" role="region" aria-label={`${monthName(schedule.month)} ${schedule.year} duty calendar`}>
      <div className="weekday-row">{weekdays.map((day) => <div key={day}>{day.slice(0, 3)}</div>)}</div>
      <div className="month-grid">{calendarItems.map((shift, index) => shift ? <ShiftCell key={shift.id} shift={shift} schedule={schedule} onSelect={setSelected} /> : <div className="calendar-blank" key={`blank-${index}`} />)}</div>
    </div>
    <ShiftDetailsDialog schedule={schedule} shift={selected} onClose={() => setSelected(null)} />
  </>
}

function ShiftCell({ shift, schedule, onSelect }: { shift: Shift; schedule: Schedule; onSelect: (shift: Shift) => void }) {
  const state = shiftState(shift, schedule)
  const mine = shift.assignments.some((a) => a.isMine)
  const today = isToday(shift.dutyDate)
  return <button className={`day-cell day-cell--${state} ${mine ? 'day-cell--mine' : ''}`} onClick={() => onSelect(shift)} aria-label={`${today ? 'Today, ' : ''}${fullDate(shift.dutyDate)}, ${stateLabel(state)}. ${assignmentLabel(shift)}`}>
    <span className="day-cell__top"><span className={today ? 'today-number' : ''}>{Number(shift.dutyDate.slice(-2))}</span></span>
    <span className="day-cell__events">{shift.assignments.length ? shift.assignments.map((assignment) => <span className={`calendar-event ${assignment.isMine ? 'calendar-event--mine' : 'calendar-event--assigned'}`} key={assignment.id}>{assignment.isMine && <Check size={11} />}<span className="assignee-full">{assignment.firstName} {assignment.lastName}</span><span className="assignee-short" aria-hidden="true">{assignment.firstName} {assignment.lastName[0]}.</span></span>) : <span className={`calendar-event calendar-event--${state}`}>{emptyEventLabel(state)}</span>}</span>
  </button>
}

function ShiftDetailsDialog({ schedule, shift, onClose }: { schedule: Schedule; shift: Shift | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const toast = useToast()
  const refresh = async () => { await queryClient.invalidateQueries({ queryKey: ['schedule'] }); await queryClient.invalidateQueries({ queryKey: ['summary'] }); onClose() }
  const assignMe = useMutation({ mutationFn: () => api(`/api/shifts/${shift?.id}/assignments/me`, { method: 'POST' }), onSuccess: async () => { toast('Shift added to your schedule.'); await refresh() } })
  const removeMe = useMutation({ mutationFn: () => api(`/api/shifts/${shift?.id}/assignments/me`, { method: 'DELETE' }), onSuccess: async () => { toast('Shift removed from your schedule.'); await refresh() } })
  if (!shift) return <Dialog open={false} onClose={onClose} title="Shift details"><span /></Dialog>
  const mine = shift.assignments.find((a) => a.isMine)
  const isOpen = schedule.status === 'OpenForSelection' && shift.assignments.length < shift.requiredStaffCount
  const mutationError = assignMe.error ?? removeMe.error
  return <Dialog open={!!shift} onClose={onClose} title={fullDate(shift.dutyDate)} className="shift-dialog">
    <div className="shift-dialog__summary">
      <div><span className="eyebrow">Night duty</span><strong><Clock3 size={18} />{timeRange(shift, schedule.timeZone)}</strong><small>Times shown in {friendlyZone(schedule.timeZone)}</small></div>
      <div className={`capacity capacity--${shiftState(shift, schedule)}`}><Users size={18} /><span><b>{shift.assignments.length} of {shift.requiredStaffCount}</b> assigned</span></div>
    </div>
    <section className="dialog-section"><h3>Assigned team</h3>{shift.assignments.length ? <ul className="assignee-list">{shift.assignments.map((assignment) => <li key={assignment.id}><span className="avatar">{assignment.firstName[0]}{assignment.lastName[0]}</span><span><strong>{assignment.firstName} {assignment.lastName}{assignment.isMine && <em>You</em>}</strong><small>{assignment.roomNumber ? `Room ${assignment.roomNumber}` : 'Room not listed'} · {assignment.status}</small></span></li>)}</ul> : <p className="muted">No resident assistants are assigned yet.</p>}</section>
    <p className="ownership-note">You can only add or remove your own assignment.</p>
    {mutationError && <p className="inline-error" role="alert">{mutationError instanceof ApiError ? mutationError.problem.title : 'The change could not be saved.'}</p>}
    <div className="dialog-actions">
      {mine ? <button className="button button--danger-quiet" onClick={() => removeMe.mutate()} disabled={removeMe.isPending || (schedule.status !== 'OpenForSelection' && !schedule.configuration.allowSelfRemovalAfterClose)}>{removeMe.isPending ? 'Removing…' : 'Remove my assignment'}</button>
        : <button className="button button--primary" onClick={() => assignMe.mutate()} disabled={!isOpen || assignMe.isPending}>{assignMe.isPending ? 'Adding…' : isOpen ? 'Select this shift' : 'Not available'}</button>}
      <button className="button button--quiet" onClick={onClose}>Close</button>
    </div>
  </Dialog>
}

function shiftState(shift: Shift, schedule: Schedule) {
  if (shift.assignments.some((a) => a.isMine)) return 'mine'
  if (schedule.status !== 'OpenForSelection') return 'closed'
  if (shift.assignments.length >= shift.requiredStaffCount) return 'full'
  return 'open'
}
function stateLabel(state: string) { return ({ mine: 'Assigned to you', closed: 'Selection closed', full: 'Fully staffed', open: 'Opening available' } as Record<string, string>)[state] }
function emptyEventLabel(state: string) { return ({ mine: 'Your shift', closed: 'Unassigned', full: 'Full', open: 'Open shift' } as Record<string, string>)[state] }
function assignmentLabel(shift: Shift) { return shift.assignments.length ? shift.assignments.map((a) => `${a.firstName} ${a.lastName}`).join(', ') : 'No one assigned' }
function dateValue(value: string) { const [year, month, day] = value.split('-').map(Number); return new Date(year, month - 1, day, 12) }
function isToday(value: string) { const date = dateValue(value); const now = new Date(); return date.getFullYear() === now.getFullYear() && date.getMonth() === now.getMonth() && date.getDate() === now.getDate() }
function fullDate(value: string) { return dateValue(value).toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }) }
function friendlyZone(zone: string) { return zone.split('/').pop()?.replace('_', ' ') ?? zone }
