using RaDuty.Domain;

namespace RaDuty.Application;

public sealed record CurrentIdentity(Guid UserId);

public interface ICurrentIdentityAccessor
{
    CurrentIdentity GetRequired();
}

public sealed record CurrentUserDto(Guid Id, string SchoolEmail, string FirstName, string LastName,
    string? RoomNumber, string? PhoneNumber, HallRole Role, bool IsActive, Guid ResidenceHallId,
    string ResidenceHallName, bool MustChangePassword = false);

public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);
public sealed record LoginResult(bool MustChangePassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record BootstrapAdminRequest(string Email, string FirstName, string LastName, string Password);
public sealed record CreateStaffAccountRequest(string Email, string FirstName, string LastName, HallRole Role,
    string? RoomNumber, string? PhoneNumber);
public sealed record ProvisionedAccountDto(ResidentAssistantDto User, string TemporaryPassword);
public sealed record TemporaryPasswordDto(string TemporaryPassword);
public sealed record AuthenticatedAccountDto(Guid UserId, string SchoolEmail, string FirstName, string LastName,
    HallRole Role, string SecurityStamp, bool MustChangePassword);

public sealed record UpdateProfileRequest(string? PhoneNumber);

public sealed record AssignmentDto(Guid Id, Guid UserId, string FirstName, string LastName, string? RoomNumber,
    AssignmentStatus Status, string? Notes, bool IsMine);

public sealed record ShiftDto(Guid Id, DateOnly DutyDate, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    int RequiredStaffCount, ShiftStatus Status, byte[] RowVersion, IReadOnlyList<AssignmentDto> Assignments);

public sealed record ScheduleConfigurationDto(int RequiredStaffPerShift, int MaximumShiftsPerUser,
    int MaximumWeekendShiftsPerUser, bool AllowConsecutiveShifts, bool AllowSelfRemovalAfterClose,
    bool RequiresApproval, bool FirstComeFirstServed);

public sealed record ScheduleDto(Guid Id, Guid ResidenceHallId, string ResidenceHallName, string TimeZone,
    int Year, int Month, ScheduleStatus Status, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt,
    DateTimeOffset? PublishedAt, ScheduleConfigurationDto Configuration, IReadOnlyList<ShiftDto> Shifts);

public sealed record ScheduleSummaryDto(int TotalShifts, int OpenShifts, int UnfilledPositions, int MyShiftCount,
    int MyWeekendShiftCount, IReadOnlyList<ShiftDto> MyUpcomingShifts);

public sealed record ResidentAssistantDto(Guid Id, string FirstName, string LastName, string SchoolEmail,
    string? RoomNumber, string? PhoneNumber, HallRole Role, bool IsActive, int ShiftCount = 0);

public sealed record DistributionDto(Guid UserId, string Name, int TotalShifts, int WeekendShifts, string Balance);

public sealed record AuditLogDto(Guid Id, DateTimeOffset OccurredAt, string Actor, string Action,
    string EntityType, string EntityId, string? Before, string? After, string? CorrelationId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record GenerateScheduleRequest(int RequiredStaffPerShift = 1, int MaximumShiftsPerUser = 6,
    int MaximumWeekendShiftsPerUser = 3, bool AllowConsecutiveShifts = false,
    bool AllowSelfRemovalAfterClose = false, bool RequiresApproval = false, bool FirstComeFirstServed = true);

public sealed record UpdateScheduleRequest(int RequiredStaffPerShift, int MaximumShiftsPerUser,
    int MaximumWeekendShiftsPerUser, bool AllowConsecutiveShifts, bool AllowSelfRemovalAfterClose,
    bool RequiresApproval, bool FirstComeFirstServed);

public sealed record AdminAssignmentRequest(Guid UserId, string? Notes, bool OverrideRules = false);
public sealed record UpdateShiftRequest(int RequiredStaffCount, ShiftStatus Status, byte[] RowVersion);
public sealed record UpdateUserRequest(string? RoomNumber, string? PhoneNumber, HallRole Role, bool IsActive);

public sealed record DormResidentDto(Guid Id, string FirstName, string LastName);
public sealed record DormRoomCheckSummaryDto(Guid Id, Guid CheckedByUserId, string CheckedByName, DateTimeOffset CheckedAt, int PhotoCount = 0);
public sealed record DormRoomDto(Guid Id, string RoomCode, string RoomLetter,
    IReadOnlyList<DormResidentDto> Residents, DormRoomCheckSummaryDto? LatestCheck);
public sealed record DormSuiteDto(string SuiteNumber, IReadOnlyList<DormRoomDto> Rooms);
public sealed record DormRoomCheckDto(Guid Id, Guid DormRoomId, string RoomCode, Guid CheckedByUserId,
    string CheckedByName, DateTimeOffset CheckedAt, bool IsRoomClean, bool IsAllFurniturePresent,
    bool IsSmokeDetectorClear, bool IsRoomOdorFree, bool IsRoomTrashFree, bool? IsCommonAreaClean,
    bool IsRoomAlcoholFree, bool IsRoomDamageFree, string? Notes, int PhotoCount = 0);
public sealed record DormRoomReportDto(Guid Id, string RoomCode, string RoomLetter,
    IReadOnlyList<DormResidentDto> Residents, DormRoomCheckDto? LatestCheck);
public sealed record DormSuiteReportDto(string SuiteNumber, IReadOnlyList<DormRoomReportDto> Rooms);
public sealed record DormCheckReportDto(string ResidenceHallName, IReadOnlyList<DormSuiteReportDto> Suites);
public sealed record SubmitDormRoomCheckRequest(bool IsRoomClean, bool IsAllFurniturePresent,
    bool IsSmokeDetectorClear, bool IsRoomOdorFree, bool IsRoomTrashFree, bool? IsCommonAreaClean,
    bool IsRoomAlcoholFree, bool IsRoomDamageFree, string? Notes);
public sealed record DormCheckPhotoUpload(string FileName, string ContentType, long Length, Stream Content);
public sealed record DormCheckPhotoDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
public sealed record DormCheckPhotoContentDto(byte[] Content, string ContentType, string FileName);
public sealed record DormCheckResetDto(int DeletedChecks, int DeletedPhotos);
public sealed record DormRosterWorkbookUpload(string FileName, long Length, Stream Content);
public sealed record DormRosterImportIssueDto(int? RowNumber, string Message);
public sealed record DormRosterChangeDto(string Type, string FirstName, string LastName, string? FromRoom, string? ToRoom);
public sealed record DormRosterImportPreviewDto(string FileName, int RowsRead, int IgnoredRows, int ResidentCount,
    int OccupiedRooms, int AddedResidents, int RemovedResidents, int MovedResidents, int UpdatedResidents,
    int UnchangedResidents, bool CanApply, IReadOnlyList<DormRosterImportIssueDto> Issues,
    IReadOnlyList<DormRosterChangeDto> Changes);
public sealed record ManagedDormResidentDto(Guid Id, string FirstName, string LastName, Guid DormRoomId,
    string RoomCode, string? SportOrActivity);
public sealed record DormRoomOptionDto(Guid Id, string RoomCode, int Occupancy, int Capacity = 2);
public sealed record SaveDormResidentRequest(string FirstName, string LastName, Guid DormRoomId, string? SportOrActivity);

public interface ICurrentUserService
{
    Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken);
}

public interface IAccountService
{
    Task<AuthenticatedAccountDto> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthenticatedAccountDto> BootstrapAdminAsync(BootstrapAdminRequest request, string? bootstrapToken,
        CancellationToken cancellationToken);
    Task<AuthenticatedAccountDto> ChangePasswordAsync(Guid userId, ChangePasswordRequest request,
        CancellationToken cancellationToken);
    Task<ProvisionedAccountDto> CreateAsync(CreateStaffAccountRequest request, CancellationToken cancellationToken);
    Task<TemporaryPasswordDto> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShiftDto>> GetMyShiftsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ResidentAssistantDto>> GetDirectoryAsync(string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResidentAssistantDto>> GetUsersAsync(string? search, CancellationToken cancellationToken);
    Task<ResidentAssistantDto> GetResidentAssistantAsync(Guid id, CancellationToken cancellationToken);
    Task<ResidentAssistantDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
}

public interface IScheduleService
{
    Task<ScheduleDto> GetAsync(int year, int month, CancellationToken cancellationToken);
    Task<ScheduleSummaryDto> GetSummaryAsync(int year, int month, CancellationToken cancellationToken);
    Task<AssignmentDto> AssignMeAsync(Guid shiftId, CancellationToken cancellationToken);
    Task RemoveMeAsync(Guid shiftId, CancellationToken cancellationToken);
    Task<ScheduleDto> GenerateAsync(int year, int month, GenerateScheduleRequest request, CancellationToken cancellationToken);
    Task<ScheduleDto> UpdateAsync(Guid periodId, UpdateScheduleRequest request, CancellationToken cancellationToken);
    Task<ScheduleDto> TransitionAsync(Guid periodId, ScheduleStatus status, CancellationToken cancellationToken);
    Task<AssignmentDto> AssignAsync(Guid shiftId, AdminAssignmentRequest request, CancellationToken cancellationToken);
    Task RemoveAssignmentAsync(Guid shiftId, Guid assignmentId, CancellationToken cancellationToken);
    Task<ShiftDto> UpdateShiftAsync(Guid shiftId, UpdateShiftRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShiftDto>> GetUnfilledAsync(Guid periodId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DistributionDto>> GetDistributionAsync(Guid periodId, CancellationToken cancellationToken);
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(string? action, int page, int pageSize, CancellationToken cancellationToken);
}

public interface ISchedulePdfService
{
    byte[] Render(ScheduleDto schedule, DateTimeOffset generatedAt);
}

public interface IDormCheckService
{
    Task<IReadOnlyList<DormSuiteDto>> GetSuitesAsync(CancellationToken cancellationToken);
    Task<DormCheckReportDto> GetReportAsync(CancellationToken cancellationToken);
    Task<DormRoomCheckDto> SubmitAsync(Guid roomId, SubmitDormRoomCheckRequest request, CancellationToken cancellationToken);
    Task<DormCheckResetDto> ResetAsync(CancellationToken cancellationToken);
}

public interface IDormCheckPdfService
{
    byte[] Render(DormCheckReportDto report, DateTimeOffset generatedAt);
}

public interface IDormCheckPhotoService
{
    Task<IReadOnlyList<DormCheckPhotoDto>> AddAsync(Guid checkId, IReadOnlyList<DormCheckPhotoUpload> photos, CancellationToken cancellationToken);
    Task<DormCheckPhotoContentDto> GetAsync(Guid photoId, CancellationToken cancellationToken);
}

public interface IDormRosterImportService
{
    Task<DormRosterImportPreviewDto> PreviewAsync(DormRosterWorkbookUpload upload, CancellationToken cancellationToken);
    Task<DormRosterImportPreviewDto> ApplyAsync(DormRosterWorkbookUpload upload, CancellationToken cancellationToken);
}

public interface IDormResidentManagementService
{
    Task<IReadOnlyList<ManagedDormResidentDto>> GetAsync(string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<DormRoomOptionDto>> GetRoomsAsync(CancellationToken cancellationToken);
    Task<ManagedDormResidentDto> CreateAsync(SaveDormResidentRequest request, CancellationToken cancellationToken);
    Task<ManagedDormResidentDto> UpdateAsync(Guid residentId, SaveDormResidentRequest request, CancellationToken cancellationToken);
    Task RemoveAsync(Guid residentId, CancellationToken cancellationToken);
}

public sealed class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
