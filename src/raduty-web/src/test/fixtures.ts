import type { CurrentUser, Schedule, Shift } from '../types'

export const raUser: CurrentUser = {
  id: 'user-1', entraObjectId: 'oid-1', schoolEmail: 'jlee@university.edu', firstName: 'Jordan', lastName: 'Lee',
  roomNumber: '214', phoneNumber: '312-555-0102', role: 'ResidentAssistant', isActive: true,
  residenceHallId: 'hall-1', residenceHallName: 'Eltse Hall',
}
export const directorUser: CurrentUser = { ...raUser, id: 'director-1', firstName: 'Marisol', lastName: 'Reyes', role: 'HallDirector' }

export function makeShift(overrides: Partial<Shift> = {}): Shift {
  return {
    id: 'shift-1', dutyDate: '2026-08-03', startsAt: '2026-08-04T02:00:00Z', endsAt: '2026-08-04T05:00:00Z',
    requiredStaffCount: 1, status: 'Open', rowVersion: '', assignments: [], ...overrides,
  }
}

export function makeSchedule(shifts: Shift[] = [makeShift()]): Schedule {
  return {
    id: 'schedule-1', residenceHallId: 'hall-1', residenceHallName: 'Eltse Hall', timeZone: 'America/Chicago',
    year: 2026, month: 8, status: 'OpenForSelection',
    configuration: { requiredStaffPerShift: 1, maximumShiftsPerUser: 6, maximumWeekendShiftsPerUser: 3,
      allowConsecutiveShifts: false, allowSelfRemovalAfterClose: false, requiresApproval: false, firstComeFirstServed: true },
    shifts,
  }
}
