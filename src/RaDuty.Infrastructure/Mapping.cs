using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

internal static class Mapping
{
    public static AssignmentDto ToDto(this ShiftAssignment assignment, Guid currentUserId) => new(
        assignment.Id, assignment.UserId, assignment.User.FirstName, assignment.User.LastName,
        assignment.User.RoomNumber, assignment.Status, assignment.Notes, assignment.UserId == currentUserId);

    public static ShiftDto ToDto(this DutyShift shift, Guid currentUserId) => new(
        shift.Id, shift.DutyDate, shift.StartsAt, shift.EndsAt, shift.RequiredStaffCount,
        shift.Status, shift.RowVersion, shift.Assignments.Where(x => x.RemovedAt == null).Select(x => x.ToDto(currentUserId)).ToList());

    public static ScheduleDto ToDto(this SchedulePeriod period, Guid currentUserId) => new(
        period.Id, period.ResidenceHallId, period.ResidenceHall.Name, period.ResidenceHall.TimeZone,
        period.Year, period.Month, period.Status, period.OpensAt, period.ClosesAt, period.PublishedAt,
        new ScheduleConfigurationDto(period.RequiredStaffPerShift, period.MaximumShiftsPerUser,
            period.MaximumWeekendShiftsPerUser, period.AllowConsecutiveShifts, period.AllowSelfRemovalAfterClose,
            period.RequiresApproval, period.FirstComeFirstServed),
        period.Shifts.OrderBy(x => x.DutyDate).Select(x => x.ToDto(currentUserId)).ToList());
}
