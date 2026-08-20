namespace RaDuty.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class User : Entity
{
    public required string SchoolEmail { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? RoomNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public HallRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HallMembership> HallMemberships { get; set; } = [];
}

public sealed class ResidenceHall : Entity
{
    public required string Name { get; set; }
    public string TimeZone { get; set; } = "America/Chicago";
    public bool IsActive { get; set; } = true;
    public ICollection<HallMembership> Memberships { get; set; } = [];
    public ICollection<SchedulePeriod> SchedulePeriods { get; set; } = [];
    public ICollection<DormRoom> DormRooms { get; set; } = [];
}

public sealed class HallMembership : Entity
{
    public Guid ResidenceHallId { get; set; }
    public ResidenceHall ResidenceHall { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public HallRole HallRole { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SchedulePeriod : Entity
{
    public Guid ResidenceHallId { get; set; }
    public ResidenceHall ResidenceHall { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public ScheduleStatus Status { get; private set; } = ScheduleStatus.OpenForSelection;
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public int RequiredStaffPerShift { get; set; } = 1;
    public int MaximumShiftsPerUser { get; set; } = 6;
    public int MaximumWeekendShiftsPerUser { get; set; } = 3;
    public bool AllowConsecutiveShifts { get; set; }
    public bool AllowSelfRemovalAfterClose { get; set; }
    public bool RequiresApproval { get; set; }
    public bool FirstComeFirstServed { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<DutyShift> Shifts { get; set; } = [];

    public void SetInitialStatusForSeed(ScheduleStatus status) => Status = status;
}

public sealed class DutyShift : Entity
{
    public Guid SchedulePeriodId { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public DateOnly DutyDate { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int RequiredStaffCount { get; set; } = 1;
    public bool IsLocked { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ShiftAssignment> Assignments { get; set; } = [];
}

public sealed class ShiftAssignment : Entity
{
    public Guid DutyShiftId { get; set; }
    public DutyShift DutyShift { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Confirmed;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid AssignedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public bool IsActive => RemovedAt is null;
}

public sealed class AuditLog : Entity
{
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class DormRoom : Entity
{
    public Guid ResidenceHallId { get; set; }
    public ResidenceHall ResidenceHall { get; set; } = null!;
    public required string SuiteNumber { get; set; }
    public required string RoomLetter { get; set; }
    public ICollection<DormResident> Residents { get; set; } = [];
    public ICollection<DormRoomCheck> Checks { get; set; } = [];
}

public sealed class DormResident : Entity
{
    public Guid DormRoomId { get; set; }
    public DormRoom DormRoom { get; set; } = null!;
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? SportOrActivity { get; set; }
    public int? SourceRow { get; set; }
}

public sealed class DormRoomCheck : Entity
{
    public Guid DormRoomId { get; set; }
    public DormRoom DormRoom { get; set; } = null!;
    public Guid CheckedByUserId { get; set; }
    public User CheckedByUser { get; set; } = null!;
    public bool IsRoomClean { get; set; }
    public bool IsAllFurniturePresent { get; set; }
    public bool IsSmokeDetectorClear { get; set; }
    public bool IsRoomOdorFree { get; set; }
    public bool IsRoomTrashFree { get; set; }
    public bool? IsCommonAreaClean { get; set; }
    public bool IsRoomAlcoholFree { get; set; }
    public bool IsRoomDamageFree { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<DormCheckPhoto> Photos { get; set; } = [];
}

public sealed class DormCheckPhoto : Entity
{
    public Guid DormRoomCheckId { get; set; }
    public DormRoomCheck DormRoomCheck { get; set; } = null!;
    public required string OriginalFileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DomainRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
