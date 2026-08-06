import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, Download } from 'lucide-react'
import { Link } from 'react-router-dom'
import { api, downloadPdf, ApiError } from '../api'
import type { CurrentUser, Schedule, ScheduleSummary, ScheduleStatus } from '../types'
import { ScheduleCalendar } from '../components/ScheduleCalendar'
import { monthName } from '../scheduleFormat'
import { CalendarSkeleton, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'

export function SchedulePage({ user }: { user: CurrentUser }) {
  const today = new Date()
  const period = { year: today.getFullYear(), month: today.getMonth() + 1 }
  const [exporting, setExporting] = useState(false)
  const toast = useToast()
  const schedule = useQuery({ queryKey: ['schedule', period.year, period.month], queryFn: ({ signal }) => api<Schedule>(`/api/schedules/${period.year}/${period.month}`, {}, signal) })
  const summary = useQuery({ queryKey: ['summary', period.year, period.month], queryFn: ({ signal }) => api<ScheduleSummary>(`/api/schedules/${period.year}/${period.month}/summary`, {}, signal), enabled: !!schedule.data })

  async function exportSchedule() {
    setExporting(true)
    try {
      await downloadPdf(`/api/schedules/${period.year}/${period.month}/pdf`, `${user.residenceHallName}-${period.year}-${period.month}-night-duty.pdf`)
      toast('Schedule PDF downloaded.')
    } catch (error) {
      toast(error instanceof ApiError ? error.problem.title : 'PDF export failed.', 'error')
    } finally {
      setExporting(false)
    }
  }

  return <div className="calendar-page">
    <header className="calendar-page-toolbar">
      <Link className="calendar-home-button" to="/" aria-label="Back to home"><ArrowLeft size={20} /></Link>
      <div className="calendar-month-title" aria-label="Current schedule month"><strong>{monthName(period.month)}</strong><span>{period.year}</span></div>
      <div className="calendar-toolbar-actions">
        {schedule.data && <><span className={`simple-status simple-status--${schedule.data.status}`}><i />{statusLabel(schedule.data.status)}</span><span className="calendar-shift-count"><strong>{summary.data?.myShiftCount ?? '—'}</strong>/{schedule.data.configuration.maximumShiftsPerUser} nights</span></>}
        <button className="quiet-icon-button" onClick={exportSchedule} disabled={!schedule.data || exporting} aria-label={exporting ? 'Preparing PDF' : 'Export schedule PDF'} title="Export PDF"><Download size={19} /></button>
      </div>
    </header>

    <section className="calendar-stage">
      {schedule.isLoading && <CalendarSkeleton />}
      {schedule.isError && <ErrorState title={schedule.error instanceof ApiError && schedule.error.problem.code === 'SCHEDULE_NOT_FOUND' ? 'No schedule for this month' : 'Schedule unavailable'} message={schedule.error instanceof ApiError ? schedule.error.problem.title : 'We could not load this schedule.'} onRetry={() => schedule.refetch()} />}
      {schedule.data && <>
        <p className="calendar-help">Tap a day to see the assigned team or change your own shift.</p>
        <ScheduleCalendar schedule={schedule.data} user={user} />
        <div className="simple-calendar-key" aria-label="Calendar key"><span className="key-open">Open</span><span className="key-mine">Mine</span><span className="key-full">Assigned</span></div>
      </>}
    </section>
  </div>
}

function statusLabel(status: ScheduleStatus) {
  return ({ Draft: 'Draft', OpenForSelection: 'Selection open', Closed: 'Selection closed', Published: 'Published', Archived: 'Archived' } as Record<ScheduleStatus, string>)[status]
}
