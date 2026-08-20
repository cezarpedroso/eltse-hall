using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class ShiftTimeFactoryTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    [Theory]
    [InlineData(2026, 8, 3, 21, 0)]
    [InlineData(2026, 8, 9, 21, 0)]
    public void Weeknight_shift_uses_real_overnight_timestamps(int year, int month, int day, int startHour, int endHour)
    {
        var date = new DateOnly(year, month, day);
        var result = ShiftTimeFactory.Create(date, Zone);
        var start = TimeZoneInfo.ConvertTime(result.StartsAt, Zone);
        var end = TimeZoneInfo.ConvertTime(result.EndsAt, Zone);
        Assert.Equal(startHour, start.Hour);
        Assert.Equal(endHour, end.Hour);
        Assert.Equal(date.AddDays(1), DateOnly.FromDateTime(end.DateTime));
        Assert.True(result.EndsAt > result.StartsAt);
    }

    [Theory]
    [InlineData(2026, 8, 7)]
    [InlineData(2026, 8, 8)]
    public void Friday_and_Saturday_end_at_2am_next_day(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        var result = ShiftTimeFactory.Create(date, Zone);
        var end = TimeZoneInfo.ConvertTime(result.EndsAt, Zone);
        Assert.Equal(2, end.Hour);
        Assert.Equal(date.AddDays(1), DateOnly.FromDateTime(end.DateTime));
        Assert.Equal(TimeSpan.FromHours(5), result.EndsAt - result.StartsAt);
    }
}

public sealed class ScheduleMonthWindowTests
{
    [Theory]
    [InlineData(2026, 12, 2026, 12, true)]
    [InlineData(2027, 1, 2026, 12, true)]
    [InlineData(2027, 2, 2026, 12, true)]
    [InlineData(2027, 3, 2026, 12, false)]
    [InlineData(2026, 11, 2026, 12, false)]
    public void Includes_current_month_and_two_months_ahead(int year, int month, int currentYear,
        int currentMonth, bool expected) =>
        Assert.Equal(expected, ScheduleMonthWindow.Contains(year, month, new DateOnly(currentYear, currentMonth, 15)));

    [Fact]
    public void New_schedules_are_live_immediately() =>
        Assert.Equal(ScheduleStatus.OpenForSelection,
            new SchedulePeriod { ResidenceHallId = Guid.NewGuid(), Year = 2026, Month = 8 }.Status);
}

public sealed class AssignmentRuleTests
{
    [Fact] public void Maximum_shift_limit_is_enforced() => AssertCode("MAXIMUM_SHIFTS_REACHED", Context(active: 6));
    [Fact] public void Maximum_weekend_limit_is_enforced() => AssertCode("MAXIMUM_WEEKEND_SHIFTS_REACHED", Context(weekend: 3, date: new DateOnly(2026, 8, 7)));
    [Fact] public void Full_shift_is_rejected() => AssertCode("SHIFT_FULL", Context(capacity: 1));
    [Fact] public void Duplicate_assignment_is_rejected() => AssertCode("USER_ALREADY_ASSIGNED", Context(duplicate: true));
    [Fact] public void Inactive_user_is_rejected() => AssertCode("USER_INACTIVE", Context(activeUser: false));

    [Fact]
    public void Consecutive_night_is_rejected()
    {
        var existing = new DutyShift { DutyDate = new DateOnly(2026, 8, 11), StartsAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddHours(3) };
        AssertCode("CONSECUTIVE_SHIFT_NOT_ALLOWED", Context(date: new DateOnly(2026, 8, 12), existing: [existing]));
    }

    [Fact]
    public void Hall_Director_override_bypasses_normal_limits()
    {
        var context = Context(active: 20, weekend: 10, capacity: 5, date: new DateOnly(2026, 8, 7));
        AssignmentRuleEvaluator.EnsureCanAssign(context, directorOverride: true);
    }

    [Fact]
    public void Legacy_schedule_status_does_not_block_assignment()
    {
        var context = Context();
        context.Period.SetInitialStatusForSeed(ScheduleStatus.Draft);
        AssignmentRuleEvaluator.EnsureCanAssign(context);
    }

    [Fact]
    public void Concurrent_second_claim_is_treated_as_duplicate()
    {
        AssignmentRuleEvaluator.EnsureCanAssign(Context());
        AssertCode("USER_ALREADY_ASSIGNED", Context(duplicate: true, capacity: 1));
    }

    private static void AssertCode(string code, AssignmentRuleContext context)
    {
        var exception = Assert.Throws<DomainRuleException>(() => AssignmentRuleEvaluator.EnsureCanAssign(context));
        Assert.Equal(code, exception.Code);
    }

    private static AssignmentRuleContext Context(int active = 0, int weekend = 0, int capacity = 0,
        bool duplicate = false, bool activeUser = true, DateOnly? date = null, IReadOnlyCollection<DutyShift>? existing = null)
    {
        var period = new SchedulePeriod
        {
            ResidenceHallId = Guid.NewGuid(), Year = 2026, Month = 8, MaximumShiftsPerUser = 6,
            MaximumWeekendShiftsPerUser = 3, AllowConsecutiveShifts = false
        };
        period.SetInitialStatusForSeed(ScheduleStatus.OpenForSelection);
        var shift = new DutyShift
        {
            SchedulePeriod = period, DutyDate = date ?? new DateOnly(2026, 8, 12),
            RequiredStaffCount = 1, StartsAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddHours(3)
        };
        var user = new User { SchoolEmail = "ra@example.edu", FirstName = "Test", LastName = "RA", IsActive = activeUser };
        return new AssignmentRuleContext(period, shift, user, existing ?? [], active, weekend, capacity, duplicate);
    }
}

public sealed class PersistenceAndPdfTests
{
    [Fact]
    public void Active_assignment_has_unique_database_constraint_and_shift_has_concurrency_token()
    {
        using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var assignmentType = db.Model.FindEntityType(typeof(ShiftAssignment))!;
        Assert.Contains(assignmentType.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([nameof(ShiftAssignment.DutyShiftId), nameof(ShiftAssignment.UserId)]));
        Assert.True(db.Model.FindEntityType(typeof(DutyShift))!.FindProperty(nameof(DutyShift.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Pdf_service_produces_a_pdf_document()
    {
        var shift = new ShiftDto(Guid.NewGuid(), new DateOnly(2026, 8, 7), DateTimeOffset.Parse("2026-08-08T02:00:00Z"),
            DateTimeOffset.Parse("2026-08-08T07:00:00Z"), 1, ShiftStatus.Open, [], []);
        var schedule = new ScheduleDto(Guid.NewGuid(), Guid.NewGuid(), "Eltse Hall", "America/Chicago", 2026, 8,
            ScheduleStatus.Draft, null, null, null,
            new ScheduleConfigurationDto(1, 6, 3, false, false, false, true), [shift]);
        var directory = new[]
        {
            new ResidentAssistantDto(Guid.NewGuid(), "Jordan", "Lee", "JordanLee@wmpenn.edu", "ELTS-09D",
                "641-555-0101", HallRole.ResidentAssistant, true, 4),
            new ResidentAssistantDto(Guid.NewGuid(), "Carol", "Ocker", "Carol.Ocker@wmpenn.edu", null,
                "641-555-0102", HallRole.HallDirector, true)
        };
        var bytes = new SchedulePdfService().Render(schedule, directory, DateTimeOffset.UtcNow);
        Assert.True(bytes.Length > 1_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Dorm_check_pdf_contains_checked_and_unchecked_rooms()
    {
        var roomId = Guid.NewGuid();
        var check = new DormRoomCheckDto(Guid.NewGuid(), roomId, "ELTS-01A", Guid.NewGuid(), "Jordan Lee",
            DateTimeOffset.Parse("2026-08-05T05:00:00Z"), true, false, true, true, true, null, true, false,
            "Desk corner has minor damage.");
        var residents = new[] { new DormResidentDto(Guid.NewGuid(), "Alex", "Rivera"), new DormResidentDto(Guid.NewGuid(), "Sam", "Lee") };
        var suite = new DormSuiteReportDto("01",
        [
            new DormRoomReportDto(roomId, "ELTS-01A", "A", residents, check),
            new DormRoomReportDto(Guid.NewGuid(), "ELTS-01B", "B", [], null),
            new DormRoomReportDto(Guid.NewGuid(), "ELTS-01C", "C", [], null),
            new DormRoomReportDto(Guid.NewGuid(), "ELTS-01D", "D", [], null)
        ]);

        var bytes = new DormCheckPdfService().Render(new DormCheckReportDto("Eltse Hall", [suite]), DateTimeOffset.UtcNow);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}

public sealed class AuthorizationPolicyTests
{
    [Fact]
    public void Application_roles_are_matched_exactly()
    {
        Assert.True(AuthorizationRules.IsInApplicationRole(["ResidentAssistant"], "ResidentAssistant", "HallDirector"));
        Assert.True(AuthorizationRules.IsInApplicationRole(["Admin"], "ResidentAssistant", "HallDirector", "Admin"));
        Assert.False(AuthorizationRules.IsInApplicationRole(["Student"], "ResidentAssistant", "HallDirector"));
    }
}
