import type { Shift } from './types'

export function monthName(month: number) {
  return new Date(2000, month - 1, 1).toLocaleDateString('en-US', { month: 'long' })
}

export interface SchedulePeriod { year: number; month: number }

export function currentSchedulePeriod(now = new Date()): SchedulePeriod {
  return { year: now.getFullYear(), month: now.getMonth() + 1 }
}

export function addScheduleMonths(period: SchedulePeriod, months: number): SchedulePeriod {
  const date = new Date(period.year, period.month - 1 + months, 1)
  return { year: date.getFullYear(), month: date.getMonth() + 1 }
}

export function compareSchedulePeriods(left: SchedulePeriod, right: SchedulePeriod) {
  return left.year * 12 + left.month - (right.year * 12 + right.month)
}

export function timeRange(shift: Shift, zone: string) {
  const options: Intl.DateTimeFormatOptions = { hour: 'numeric', minute: '2-digit', timeZone: zone }
  const formatter = new Intl.DateTimeFormat('en-US', options)
  return `${formatter.format(new Date(shift.startsAt))}–${formatter.format(new Date(shift.endsAt))}`
}
