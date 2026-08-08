using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class ScheduleService(RaDutyDbContext db, ICurrentUserService currentUserService) : IScheduleService
{
    public async Task<ScheduleDto> GetAsync(int year, int month, CancellationToken cancellationToken)
    {
        ValidateMonth(year, month);
        var current = await currentUserService.GetAsync(cancellationToken);
        var period = await PeriodQuery().SingleOrDefaultAsync(x => x.ResidenceHallId == current.ResidenceHallId && x.Year == year && x.Month == month, cancellationToken)
            ?? throw new AppException(404, "SCHEDULE_NOT_FOUND", "No schedule exists for this month.");
        if (period.Status == ScheduleStatus.Draft && current.Role is not HallRole.HallDirector and not HallRole.Admin)
            throw new AppException(404, "SCHEDULE_NOT_FOUND", "No schedule exists for this month.");
        return period.ToDto(current.Id);
    }

    public async Task<ScheduleSummaryDto> GetSummaryAsync(int year, int month, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var schedule = await GetAsync(year, month, cancellationToken);
        var mine = schedule.Shifts.Where(x => x.Assignments.Any(a => a.UserId == current.Id)).OrderBy(x => x.StartsAt).ToList();
        return new ScheduleSummaryDto(schedule.Shifts.Count,
            schedule.Shifts.Count(x => x.Assignments.Count < x.RequiredStaffCount),
            schedule.Shifts.Sum(x => Math.Max(0, x.RequiredStaffCount - x.Assignments.Count)), mine.Count,
            mine.Count(x => ShiftTimeFactory.IsWeekend(x.DutyDate)), mine.Where(x => x.EndsAt >= DateTimeOffset.UtcNow).Take(6).ToList());
    }

    public async Task<AssignmentDto> AssignMeAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        return await AssignCoreAsync(shiftId, current.Id, current.Id, null, false, cancellationToken);
    }

    public async Task RemoveMeAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var assignment = await db.ShiftAssignments.Include(x => x.DutyShift).ThenInclude(x => x.SchedulePeriod)
            .SingleOrDefaultAsync(x => x.DutyShiftId == shiftId && x.UserId == current.Id && x.RemovedAt == null, cancellationToken)
            ?? throw new AppException(404, "ASSIGNMENT_NOT_FOUND", "You are not assigned to this shift.");
        var period = assignment.DutyShift.SchedulePeriod;
        var canRemove = period.Status == ScheduleStatus.OpenForSelection ||
            (period.Status == ScheduleStatus.Closed && period.AllowSelfRemovalAfterClose);
        if (!canRemove) throw new AppException(422, "SELF_REMOVAL_NOT_ALLOWED", "Assignments cannot be removed at this schedule stage.");
        assignment.RemovedAt = DateTimeOffset.UtcNow;
        assignment.RemovedByUserId = current.Id;
        db.AuditLogs.Add(Audit(current.Id, "SELF_ASSIGNMENT_REMOVED", "ShiftAssignment", assignment.Id, null, new { assignment.RemovedAt }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduleDto> GenerateAsync(int year, int month, GenerateScheduleRequest request, CancellationToken cancellationToken)
    {
        ValidateMonth(year, month);
        ValidateConfiguration(request.RequiredStaffPerShift, request.MaximumShiftsPerUser, request.MaximumWeekendShiftsPerUser);
        var current = await currentUserService.GetAsync(cancellationToken);
        if (await db.SchedulePeriods.AnyAsync(x => x.ResidenceHallId == current.ResidenceHallId && x.Year == year && x.Month == month, cancellationToken))
            throw new AppException(409, "SCHEDULE_ALREADY_EXISTS", "A schedule already exists for this month.");
        var hall = await db.ResidenceHalls.SingleAsync(x => x.Id == current.ResidenceHallId, cancellationToken);
        var period = new SchedulePeriod
        {
            ResidenceHall = hall, Year = year, Month = month,
            RequiredStaffPerShift = request.RequiredStaffPerShift,
            MaximumShiftsPerUser = request.MaximumShiftsPerUser,
            MaximumWeekendShiftsPerUser = request.MaximumWeekendShiftsPerUser,
            AllowConsecutiveShifts = request.AllowConsecutiveShifts,
            AllowSelfRemovalAfterClose = request.AllowSelfRemovalAfterClose,
            RequiresApproval = request.RequiresApproval,
            FirstComeFirstServed = request.FirstComeFirstServed
        };
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(hall.TimeZone);
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var date = new DateOnly(year, month, day);
            var times = ShiftTimeFactory.Create(date, timeZone);
            period.Shifts.Add(new DutyShift
            {
                DutyDate = date, StartsAt = times.StartsAt, EndsAt = times.EndsAt,
                RequiredStaffCount = request.RequiredStaffPerShift
            });
        }
        db.SchedulePeriods.Add(period);
        db.AuditLogs.Add(Audit(current.Id, "SCHEDULE_GENERATED", "SchedulePeriod", period.Id, null, new { year, month }));
        await db.SaveChangesAsync(cancellationToken);
        return (await PeriodQuery().SingleAsync(x => x.Id == period.Id, cancellationToken)).ToDto(current.Id);
    }

    public async Task<ScheduleDto> UpdateAsync(Guid periodId, UpdateScheduleRequest request, CancellationToken cancellationToken)
    {
        ValidateConfiguration(request.RequiredStaffPerShift, request.MaximumShiftsPerUser, request.MaximumWeekendShiftsPerUser);
        var current = await currentUserService.GetAsync(cancellationToken);
        var period = await PeriodQuery().SingleOrDefaultAsync(x => x.Id == periodId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "SCHEDULE_NOT_FOUND", "Schedule not found.");
        if (period.Status is ScheduleStatus.Published or ScheduleStatus.Archived)
            throw new AppException(422, "SCHEDULE_NOT_EDITABLE", "Published or archived configuration cannot be edited.");
        var before = ConfigurationSnapshot(period);
        period.RequiredStaffPerShift = request.RequiredStaffPerShift;
        period.MaximumShiftsPerUser = request.MaximumShiftsPerUser;
        period.MaximumWeekendShiftsPerUser = request.MaximumWeekendShiftsPerUser;
        period.AllowConsecutiveShifts = request.AllowConsecutiveShifts;
        period.AllowSelfRemovalAfterClose = request.AllowSelfRemovalAfterClose;
        period.RequiresApproval = request.RequiresApproval;
        period.FirstComeFirstServed = request.FirstComeFirstServed;
        period.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(Audit(current.Id, "SCHEDULE_CONFIGURATION_UPDATED", "SchedulePeriod", period.Id, before, ConfigurationSnapshot(period)));
        await db.SaveChangesAsync(cancellationToken);
        return period.ToDto(current.Id);
    }

    public async Task<ScheduleDto> TransitionAsync(Guid periodId, ScheduleStatus status, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var period = await PeriodQuery().SingleOrDefaultAsync(x => x.Id == periodId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "SCHEDULE_NOT_FOUND", "Schedule not found.");
        if (status == ScheduleStatus.Published && period.Shifts.Any(x => x.Status != ShiftStatus.Cancelled && x.Assignments.Count(a => a.RemovedAt == null) < x.RequiredStaffCount))
            throw new AppException(422, "SCHEDULE_HAS_UNFILLED_SHIFTS", "Fill or cancel every shift before publishing the schedule.");
        var before = period.Status;
        period.TransitionTo(status, DateTimeOffset.UtcNow, current.Id);
        db.AuditLogs.Add(Audit(current.Id, $"SCHEDULE_{status.ToString().ToUpperInvariant()}", "SchedulePeriod", period.Id, new { Status = before }, new { Status = status }));
        await db.SaveChangesAsync(cancellationToken);
        return period.ToDto(current.Id);
    }

    public async Task<AssignmentDto> AssignAsync(Guid shiftId, AdminAssignmentRequest request, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        if (request.Notes?.Length > 1000) throw new AppException(400, "NOTES_TOO_LONG", "Notes must be 1,000 characters or fewer.");
        return await AssignCoreAsync(shiftId, request.UserId, current.Id, request.Notes?.Trim(), request.OverrideRules, cancellationToken);
    }

    public async Task RemoveAssignmentAsync(Guid shiftId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var assignment = await db.ShiftAssignments.Include(x => x.DutyShift).ThenInclude(x => x.SchedulePeriod)
            .SingleOrDefaultAsync(x => x.Id == assignmentId && x.DutyShiftId == shiftId && x.RemovedAt == null, cancellationToken)
            ?? throw new AppException(404, "ASSIGNMENT_NOT_FOUND", "Assignment not found.");
        EnsureSameHall(assignment.DutyShift.SchedulePeriod, current);
        assignment.RemovedAt = DateTimeOffset.UtcNow;
        assignment.RemovedByUserId = current.Id;
        db.AuditLogs.Add(Audit(current.Id, "ADMIN_ASSIGNMENT_REMOVED", "ShiftAssignment", assignment.Id, null, new { assignment.RemovedAt }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ShiftDto> UpdateShiftAsync(Guid shiftId, UpdateShiftRequest request, CancellationToken cancellationToken)
    {
        if (request.RequiredStaffCount is < 1 or > 10) throw new AppException(400, "INVALID_STAFF_COUNT", "Required staff must be between 1 and 10.");
        var current = await currentUserService.GetAsync(cancellationToken);
        var shift = await db.DutyShifts.Include(x => x.SchedulePeriod)
            .Include(x => x.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == shiftId, cancellationToken)
            ?? throw new AppException(404, "SHIFT_NOT_FOUND", "Shift not found.");
        EnsureSameHall(shift.SchedulePeriod, current);
        db.Entry(shift).Property(x => x.RowVersion).OriginalValue = request.RowVersion;
        var before = new { shift.RequiredStaffCount, shift.Status };
        shift.RequiredStaffCount = request.RequiredStaffCount;
        shift.Status = request.Status;
        shift.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(Audit(current.Id, "SHIFT_UPDATED", "DutyShift", shift.Id, before, new { shift.RequiredStaffCount, shift.Status }));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new AppException(409, "CONCURRENCY_CONFLICT", "This shift changed. Refresh and try again."); }
        return shift.ToDto(current.Id);
    }

    public async Task<IReadOnlyList<ShiftDto>> GetUnfilledAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var period = await PeriodQuery().SingleOrDefaultAsync(x => x.Id == periodId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "SCHEDULE_NOT_FOUND", "Schedule not found.");
        return period.Shifts.Where(x => x.Status != ShiftStatus.Cancelled && x.Assignments.Count(a => a.RemovedAt == null) < x.RequiredStaffCount)
            .Select(x => x.ToDto(current.Id)).ToList();
    }

    public async Task<IReadOnlyList<DistributionDto>> GetDistributionAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var period = await PeriodQuery().SingleOrDefaultAsync(x => x.Id == periodId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "SCHEDULE_NOT_FOUND", "Schedule not found.");
        var memberships = await db.HallMemberships.AsNoTracking().Include(x => x.User)
            .Where(x => x.ResidenceHallId == current.ResidenceHallId && x.IsActive && x.HallRole == HallRole.ResidentAssistant)
            .OrderBy(x => x.User.LastName).ToListAsync(cancellationToken);
        return memberships.Select(m =>
        {
            var shifts = period.Shifts.Where(s => s.Assignments.Any(a => a.UserId == m.UserId && a.RemovedAt == null)).ToList();
            var balance = shifts.Count < Math.Max(1, period.MaximumShiftsPerUser - 2) ? "Below target" : shifts.Count >= period.MaximumShiftsPerUser ? "At limit" : "On target";
            return new DistributionDto(m.UserId, $"{m.User.FirstName} {m.User.LastName}", shifts.Count,
                shifts.Count(x => ShiftTimeFactory.IsWeekend(x.DutyDate)), balance);
        }).ToList();
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(string? action, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var current = await currentUserService.GetAsync(cancellationToken);
        var actorIds = await db.HallMemberships.Where(x => x.ResidenceHallId == current.ResidenceHallId).Select(x => x.UserId).ToListAsync(cancellationToken);
        var query = db.AuditLogs.AsNoTracking().Where(x => x.ActorUserId == null || actorIds.Contains(x.ActorUserId.Value));
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action.Contains(action.Trim()));
        var total = await query.CountAsync(cancellationToken);
        var raw = await query.OrderByDescending(x => x.OccurredAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var users = await db.StaffUsers.AsNoTracking().Where(x => raw.Where(a => a.ActorUserId.HasValue).Select(a => a.ActorUserId!.Value).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        return new PagedResult<AuditLogDto>(raw.Select(x => new AuditLogDto(x.Id, x.OccurredAt,
            x.ActorUserId.HasValue && users.TryGetValue(x.ActorUserId.Value, out var name) ? name : "System",
            x.Action, x.EntityType, x.EntityId, x.OldValuesJson, x.NewValuesJson, x.CorrelationId)).ToList(), page, pageSize, total);
    }

    private async Task<AssignmentDto> AssignCoreAsync(Guid shiftId, Guid userId, Guid actorId, string? notes, bool directorOverride, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        AssignmentDto? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var shift = await db.DutyShifts.Include(x => x.SchedulePeriod).ThenInclude(x => x.ResidenceHall)
                    .Include(x => x.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(x => x.User)
                    .SingleOrDefaultAsync(x => x.Id == shiftId, cancellationToken)
                    ?? throw new AppException(404, "SHIFT_NOT_FOUND", "Shift not found.");
                EnsureSameHall(shift.SchedulePeriod, current);
                var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
                    ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found.");
                var belongsToHall = await db.HallMemberships.AnyAsync(x => x.UserId == userId && x.ResidenceHallId == current.ResidenceHallId && x.IsActive, cancellationToken);
                if (!belongsToHall) throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found in this hall.");
                var existing = await db.DutyShifts.Include(x => x.Assignments.Where(a => a.RemovedAt == null))
                    .Where(x => x.SchedulePeriodId == shift.SchedulePeriodId && x.Assignments.Any(a => a.UserId == userId && a.RemovedAt == null)).ToListAsync(cancellationToken);
                var context = new AssignmentRuleContext(shift.SchedulePeriod, shift, user, existing, existing.Count,
                    existing.Count(x => ShiftTimeFactory.IsWeekend(x.DutyDate)), shift.Assignments.Count,
                    shift.Assignments.Any(x => x.UserId == userId));
                AssignmentRuleEvaluator.EnsureCanAssign(context, directorOverride);
                var assignment = new ShiftAssignment
                {
                    DutyShift = shift, User = user, AssignedByUserId = actorId, Notes = notes,
                    Status = shift.SchedulePeriod.RequiresApproval && !directorOverride ? AssignmentStatus.Pending : AssignmentStatus.Confirmed
                };
                db.ShiftAssignments.Add(assignment);
                db.AuditLogs.Add(Audit(actorId, actorId == userId ? "SELF_ASSIGNMENT_CREATED" : "ADMIN_ASSIGNMENT_CREATED",
                    "ShiftAssignment", assignment.Id, null, new { shiftId, userId, assignment.Status }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                result = assignment.ToDto(current.Id);
            });
        }
        catch (DomainRuleException ex) { throw new AppException(422, ex.Code, ex.Message); }
        catch (DbUpdateConcurrencyException) { throw new AppException(409, "CONCURRENCY_CONFLICT", "This shift changed. Refresh and try again."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new AppException(409, "CONCURRENCY_CONFLICT", "This assignment was claimed by another request. Refresh and try again."); }
        return result!;
    }

    private IQueryable<SchedulePeriod> PeriodQuery() => db.SchedulePeriods
        .Include(x => x.ResidenceHall).Include(x => x.Shifts.OrderBy(s => s.DutyDate))
        .ThenInclude(x => x.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(x => x.User);

    private static void EnsureSameHall(SchedulePeriod period, CurrentUserDto current)
    {
        if (period.ResidenceHallId != current.ResidenceHallId) throw new AppException(404, "SCHEDULE_NOT_FOUND", "Schedule not found.");
    }

    private static void ValidateMonth(int year, int month)
    {
        if (year is < 2020 or > 2200 || month is < 1 or > 12) throw new AppException(400, "INVALID_SCHEDULE_MONTH", "Enter a valid schedule month.");
    }

    private static void ValidateConfiguration(int required, int maximum, int weekendMaximum)
    {
        if (required is < 1 or > 10 || maximum is < 1 or > 31 || weekendMaximum < 0 || weekendMaximum > maximum)
            throw new AppException(400, "INVALID_SCHEDULE_CONFIGURATION", "Schedule limits are outside the allowed range.");
    }

    private static object ConfigurationSnapshot(SchedulePeriod p) => new
    {
        p.RequiredStaffPerShift, p.MaximumShiftsPerUser, p.MaximumWeekendShiftsPerUser, p.AllowConsecutiveShifts,
        p.AllowSelfRemovalAfterClose, p.RequiresApproval, p.FirstComeFirstServed
    };

    private static AuditLog Audit(Guid actor, string action, string type, Guid id, object? before, object? after) => new()
    {
        ActorUserId = actor, Action = action, EntityType = type, EntityId = id.ToString(),
        OldValuesJson = before is null ? null : JsonSerializer.Serialize(before),
        NewValuesJson = after is null ? null : JsonSerializer.Serialize(after)
    };
}
