namespace RaDuty.Domain;

public static class ShiftTimeFactory
{
    public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) Create(DateOnly date, TimeZoneInfo timeZone)
    {
        var localStart = date.ToDateTime(new TimeOnly(21, 0), DateTimeKind.Unspecified);
        var endHour = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 2 : 0;
        var localEndDate = date.AddDays(1);
        var localEnd = localEndDate.ToDateTime(new TimeOnly(endHour, 0), DateTimeKind.Unspecified);
        return (new DateTimeOffset(localStart, timeZone.GetUtcOffset(localStart)).ToUniversalTime(),
            new DateTimeOffset(localEnd, timeZone.GetUtcOffset(localEnd)).ToUniversalTime());
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
}

public sealed record AssignmentRuleContext(
    SchedulePeriod Period,
    DutyShift Shift,
    User User,
    IReadOnlyCollection<DutyShift> ExistingShifts,
    int ActiveAssignmentCount,
    int ActiveWeekendAssignmentCount,
    int ActiveShiftAssignmentCount,
    bool IsAlreadyAssigned);

public static class AssignmentRuleEvaluator
{
    public static void EnsureCanAssign(AssignmentRuleContext context, bool directorOverride = false)
    {
        if (!context.User.IsActive) Throw("USER_INACTIVE", "Inactive users cannot be assigned.");
        if (context.IsAlreadyAssigned) Throw("USER_ALREADY_ASSIGNED", "This user is already assigned to the shift.");
        if (!directorOverride && context.Period.Status != ScheduleStatus.OpenForSelection) Throw("SCHEDULE_NOT_OPEN", "This schedule is not open for selection.");
        if (!directorOverride && context.ActiveShiftAssignmentCount >= context.Shift.RequiredStaffCount) Throw("SHIFT_FULL", "This shift is already fully staffed.");
        if (!directorOverride && context.ActiveAssignmentCount >= context.Period.MaximumShiftsPerUser) Throw("MAXIMUM_SHIFTS_REACHED", "The monthly shift limit has been reached.");
        if (!directorOverride && ShiftTimeFactory.IsWeekend(context.Shift.DutyDate) && context.ActiveWeekendAssignmentCount >= context.Period.MaximumWeekendShiftsPerUser)
            Throw("MAXIMUM_WEEKEND_SHIFTS_REACHED", "The weekend shift limit has been reached.");
        if (!directorOverride && !context.Period.AllowConsecutiveShifts && context.ExistingShifts.Any(x => Math.Abs(x.DutyDate.DayNumber - context.Shift.DutyDate.DayNumber) == 1))
            Throw("CONSECUTIVE_SHIFT_NOT_ALLOWED", "Consecutive-night assignments are not allowed.");
    }

    private static void Throw(string code, string message) => throw new DomainRuleException(code, message);
}
