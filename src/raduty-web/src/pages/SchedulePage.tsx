import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CalendarPlus, ChevronLeft, ChevronRight, Download } from 'lucide-react'
import { Link } from 'react-router-dom'
import { api, downloadPdf, ApiError } from '../api'
import type { CurrentUser, Schedule, ScheduleSummary } from '../types'
import { ScheduleCalendar } from '../components/ScheduleCalendar'
import { addScheduleMonths, compareSchedulePeriods, currentSchedulePeriod, monthName } from '../scheduleFormat'
import { CalendarSkeleton, ErrorState } from '../components/ui'
import { useToast } from '../components/toast'

export function SchedulePage({ user }: { user: CurrentUser }) {
  const currentPeriod = currentSchedulePeriod()
  const latestPeriod = addScheduleMonths(currentPeriod, 2)
  const [period, setPeriod] = useState(currentPeriod)
  const [exporting, setExporting] = useState(false)
  const toast = useToast()
  const queryClient = useQueryClient()
  const schedule = useQuery({ queryKey: ['schedule', period.year, period.month], queryFn: ({ signal }) => api<Schedule>(`/api/schedules/${period.year}/${period.month}`, {}, signal) })
  const summary = useQuery({ queryKey: ['summary', period.year, period.month], queryFn: ({ signal }) => api<ScheduleSummary>(`/api/schedules/${period.year}/${period.month}/summary`, {}, signal), enabled: !!schedule.data })
  const startSchedule = useMutation({
    mutationFn: () => api<Schedule>(`/api/admin/schedules/${period.year}/${period.month}/generate`, { method: 'POST', body: JSON.stringify({}) }),
    onSuccess: (createdSchedule) => {
      queryClient.setQueryData(['schedule', period.year, period.month], createdSchedule)
      toast(`${monthName(period.month)} schedule started.`)
    },
  })
  const isManager = user.role === 'HallDirector' || user.role === 'Admin'
  const isMissing = schedule.error instanceof ApiError && schedule.error.problem.code === 'SCHEDULE_NOT_FOUND'
  const canGoBack = compareSchedulePeriods(period, currentPeriod) > 0
  const canGoForward = compareSchedulePeriods(period, latestPeriod) < 0

  function changeMonth(offset: number) {
    const next = addScheduleMonths(period, offset)
    if (compareSchedulePeriods(next, currentPeriod) >= 0 && compareSchedulePeriods(next, latestPeriod) <= 0)
      setPeriod(next)
  }

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
      <div className="calendar-month-picker">
        <button type="button" className="calendar-month-step" onClick={() => changeMonth(-1)} disabled={!canGoBack} aria-label="Previous month"><ChevronLeft size={18} /></button>
        <div className="calendar-month-title" aria-label="Selected schedule month"><strong>{monthName(period.month)}</strong><span>{period.year}</span></div>
        <button type="button" className="calendar-month-step" onClick={() => changeMonth(1)} disabled={!canGoForward} aria-label="Next month"><ChevronRight size={18} /></button>
      </div>
      <div className="calendar-toolbar-actions">
        {schedule.data && <span className="calendar-shift-count"><strong>{summary.data?.myShiftCount ?? '—'}</strong>/{schedule.data.configuration.maximumShiftsPerUser} nights</span>}
        <button className="schedule-download-button" onClick={exportSchedule} disabled={!schedule.data || exporting} aria-label={exporting ? 'Preparing PDF' : 'Download schedule PDF'}><Download size={18} /><span>{exporting ? 'Preparing…' : 'Download PDF'}</span></button>
      </div>
    </header>

    <section className="calendar-stage">
      {schedule.isLoading && <CalendarSkeleton />}
      {schedule.isError && isMissing && isManager && <div className="state-message schedule-start-state" role="status"><CalendarPlus /><div><h2>No schedule for {monthName(period.month)} yet</h2><p>Start this month to create the live night-duty calendar for the team.</p><button className="button button--primary" onClick={() => startSchedule.mutate()} disabled={startSchedule.isPending}>{startSchedule.isPending ? 'Starting…' : 'Start schedule'}</button>{startSchedule.isError && <p className="inline-error" role="alert">{startSchedule.error instanceof ApiError ? startSchedule.error.problem.title : 'The schedule could not be started.'}</p>}</div></div>}
      {schedule.isError && (!isMissing || !isManager) && <ErrorState title={isMissing ? `No schedule for ${monthName(period.month)} yet` : 'Schedule unavailable'} message={schedule.error instanceof ApiError ? schedule.error.problem.title : 'We could not load this schedule.'} onRetry={() => schedule.refetch()} />}
      {schedule.data && <>
        <p className="calendar-help">{user.role === 'ResidentAssistant' ? 'Tap a day to see the assigned team or change your own shift.' : 'Tap a day to assign or unassign night-duty coverage.'}</p>
        <ScheduleCalendar schedule={schedule.data} user={user} />
        <div className="simple-calendar-key" aria-label="Calendar key"><span className="key-open">Open</span><span className="key-mine">Mine</span><span className="key-full">Assigned</span></div>
      </>}
    </section>
  </div>
}
