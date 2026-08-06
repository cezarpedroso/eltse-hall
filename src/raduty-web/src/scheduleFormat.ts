import type { Shift } from './types'

export function monthName(month: number) {
  return new Date(2026, month - 1, 1).toLocaleDateString('en-US', { month: 'long' })
}

export function timeRange(shift: Shift, zone: string) {
  const options: Intl.DateTimeFormatOptions = { hour: 'numeric', minute: '2-digit', timeZone: zone }
  const formatter = new Intl.DateTimeFormat('en-US', options)
  return `${formatter.format(new Date(shift.startsAt))}–${formatter.format(new Date(shift.endsAt))}`
}

