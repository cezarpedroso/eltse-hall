export type HallRole = 'ResidentAssistant' | 'HallDirector' | 'Admin'
export type ScheduleStatus = 'Draft' | 'OpenForSelection' | 'Closed' | 'Published' | 'Archived'
export type ShiftStatus = 'Open' | 'Full' | 'Cancelled'

export interface CurrentUser {
  id: string; schoolEmail: string; firstName: string; lastName: string
  roomNumber?: string; phoneNumber?: string; role: HallRole; isActive: boolean
  residenceHallId: string; residenceHallName: string; mustChangePassword?: boolean
}

export interface Assignment {
  id: string; userId: string; firstName: string; lastName: string; roomNumber?: string
  status: 'Pending' | 'Confirmed'; notes?: string; isMine: boolean
}

export interface Shift {
  id: string; dutyDate: string; startsAt: string; endsAt: string; requiredStaffCount: number
  status: ShiftStatus; rowVersion: string; assignments: Assignment[]
}

export interface Schedule {
  id: string; residenceHallId: string; residenceHallName: string; timeZone: string
  year: number; month: number; status: ScheduleStatus; opensAt?: string; closesAt?: string; publishedAt?: string
  configuration: {
    requiredStaffPerShift: number; maximumShiftsPerUser: number; maximumWeekendShiftsPerUser: number
    allowConsecutiveShifts: boolean; allowSelfRemovalAfterClose: boolean; requiresApproval: boolean
    firstComeFirstServed: boolean
  }
  shifts: Shift[]
}

export interface ScheduleSummary {
  totalShifts: number; openShifts: number; unfilledPositions: number; myShiftCount: number
  myWeekendShiftCount: number; myUpcomingShifts: Shift[]
}

export interface ResidentAssistant {
  id: string; firstName: string; lastName: string; schoolEmail: string; roomNumber?: string
  phoneNumber?: string; role: HallRole; isActive: boolean; shiftCount: number
}

export interface ProvisionedAccount { user: ResidentAssistant; temporaryPassword: string }
export interface TemporaryPassword { temporaryPassword: string }

export interface Distribution {
  userId: string; name: string; totalShifts: number; weekendShifts: number; balance: string
}

export interface AuditLog {
  id: string; occurredAt: string; actor: string; action: string; entityType: string
  entityId: string; before?: string; after?: string; correlationId?: string
}

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; total: number }

export interface DormResident { id: string; firstName: string; lastName: string }
export interface DormRoomCheckSummary { id: string; checkedByUserId: string; checkedByName: string; checkedAt: string; photoCount: number }
export interface DormRoom {
  id: string; roomCode: string; roomLetter: string; residents: DormResident[]; latestCheck?: DormRoomCheckSummary | null
}
export interface DormSuite { suiteNumber: string; rooms: DormRoom[] }
export interface DormRoomCheck extends DormRoomCheckSummary {
  dormRoomId: string; roomCode: string; isRoomClean: boolean; isAllFurniturePresent: boolean
  isSmokeDetectorClear: boolean; isRoomOdorFree: boolean; isRoomTrashFree: boolean
  isCommonAreaClean?: boolean | null; isRoomAlcoholFree: boolean; isRoomDamageFree: boolean; notes?: string; photoCount: number
}
export interface DormCheckPhoto { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string }
export interface DormCheckReset { deletedChecks: number; deletedPhotos: number }
export interface DormRosterImportIssue { rowNumber?: number | null; message: string }
export interface DormRosterChange { type: 'Added' | 'Removed' | 'Moved' | 'Updated'; firstName: string; lastName: string; fromRoom?: string | null; toRoom?: string | null }
export interface DormRosterImportPreview {
  fileName: string; rowsRead: number; ignoredRows: number; residentCount: number; occupiedRooms: number
  addedResidents: number; removedResidents: number; movedResidents: number; updatedResidents: number
  unchangedResidents: number; canApply: boolean; issues: DormRosterImportIssue[]; changes: DormRosterChange[]
}
export interface ManagedDormResident { id: string; firstName: string; lastName: string; dormRoomId: string; roomCode: string; sportOrActivity?: string | null }
export interface DormRoomOption { id: string; roomCode: string; occupancy: number; capacity: number }

export interface ProblemDetails { status: number; title: string; code: string; traceId?: string }
