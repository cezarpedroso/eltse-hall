using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public static class DevelopmentSeed
{
    private static readonly Guid HallId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string HallName = "Eltse Hall";

    public static async Task InitializeAsync(RaDutyDbContext db, CancellationToken cancellationToken = default)
    {
        var existingHall = await db.ResidenceHalls.SingleOrDefaultAsync(x => x.Id == HallId, cancellationToken);
        if (existingHall is not null)
        {
            if (existingHall.Name != HallName)
            {
                existingHall.Name = HallName;
                await db.SaveChangesAsync(cancellationToken);
            }
            await EnsureDormRosterAsync(db, existingHall, cancellationToken);
            return;
        }
        if (await db.ResidenceHalls.AnyAsync(cancellationToken)) return;

        var hall = new ResidenceHall { Id = HallId, Name = HallName, TimeZone = "America/Chicago" };
        var users = new[]
        {
            User("20000000-0000-0000-0000-000000000001", "dev-director", "Marisol", "Reyes", "mreyes@university.edu", "101", "312-555-0101", HallRole.HallDirector),
            User("20000000-0000-0000-0000-000000000002", "dev-ra-001", "Jordan", "Lee", "jlee@university.edu", "214", "312-555-0102"),
            User("20000000-0000-0000-0000-000000000003", "dev-ra-002", "Amara", "Okafor", "aokafor@university.edu", "318", "312-555-0103"),
            User("20000000-0000-0000-0000-000000000004", "dev-ra-003", "Eli", "Bennett", "ebennett@university.edu", "407", "312-555-0104"),
            User("20000000-0000-0000-0000-000000000005", "dev-ra-004", "Priya", "Shah", "pshah@university.edu", "226", "312-555-0105"),
            User("20000000-0000-0000-0000-000000000006", "dev-ra-005", "Mateo", "Rivera", "mrivera@university.edu", "512", "312-555-0106"),
            User("20000000-0000-0000-0000-000000000007", "dev-ra-006", "Nora", "Kim", "nkim@university.edu", "304", "312-555-0107"),
            User("20000000-0000-0000-0000-000000000008", "dev-ra-007", "Caleb", "Moore", "cmoore@university.edu", "119", "312-555-0108"),
            User("20000000-0000-0000-0000-000000000009", "dev-ra-008", "Sofia", "Patel", "spatel@university.edu", "421", "312-555-0109")
        };
        foreach (var user in users)
        {
            hall.Memberships.Add(new HallMembership { ResidenceHall = hall, User = user, HallRole = user.Role });
        }
        db.Add(hall);
        db.AddRange(users);

        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(hall.TimeZone));
        var current = CreatePeriod(hall, localNow.Year, localNow.Month, ScheduleStatus.OpenForSelection);
        current.OpensAt = DateTimeOffset.UtcNow.AddDays(-3);
        for (var index = 0; index < Math.Min(14, current.Shifts.Count); index++)
        {
            var shift = current.Shifts.ElementAt(index);
            var user = users[1 + index % 8];
            shift.Assignments.Add(new ShiftAssignment { DutyShift = shift, User = user, AssignedByUserId = user.Id });
            shift.Status = ShiftStatus.Full;
        }

        var previousDate = new DateOnly(localNow.Year, localNow.Month, 1).AddMonths(-1);
        var previous = CreatePeriod(hall, previousDate.Year, previousDate.Month, ScheduleStatus.Published);
        previous.PublishedAt = DateTimeOffset.UtcNow.AddDays(-20);
        previous.PublishedByUserId = users[0].Id;
        for (var index = 0; index < previous.Shifts.Count; index++)
        {
            var shift = previous.Shifts.ElementAt(index);
            var user = users[1 + index % 8];
            shift.Assignments.Add(new ShiftAssignment { DutyShift = shift, User = user, AssignedByUserId = users[0].Id });
            shift.Status = ShiftStatus.Full;
        }
        db.AddRange(current, previous);
        await db.SaveChangesAsync(cancellationToken);
        await EnsureDormRosterAsync(db, hall, cancellationToken);
    }

    private static async Task EnsureDormRosterAsync(RaDutyDbContext db, ResidenceHall hall, CancellationToken cancellationToken)
    {
        var rooms = await db.DormRooms.Where(x => x.ResidenceHallId == hall.Id).ToListAsync(cancellationToken);
        var byCode = rooms.ToDictionary(x => $"ELTS-{x.SuiteNumber}{x.RoomLetter}", StringComparer.OrdinalIgnoreCase);
        for (var suite = 1; suite <= 25; suite++)
        {
            foreach (var roomLetter in new[] { "A", "B", "C", "D" })
            {
                var room = new DormRoom { ResidenceHall = hall, SuiteNumber = suite.ToString("00"), RoomLetter = roomLetter };
                var code = $"ELTS-{room.SuiteNumber}{room.RoomLetter}";
                if (byCode.ContainsKey(code)) continue;
                db.DormRooms.Add(room);
                rooms.Add(room);
                byCode.Add(code, room);
            }
        }

        if (!await db.DormResidents.AnyAsync(x => x.DormRoom.ResidenceHallId == hall.Id, cancellationToken))
        {
            await using var stream = typeof(DevelopmentSeed).Assembly.GetManifestResourceStream("RaDuty.Infrastructure.SeedData.elts-residents.json")
                ?? throw new InvalidOperationException("The ELTS resident seed roster is missing.");
            var roster = await JsonSerializer.DeserializeAsync<List<ResidentSeed>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? [];
            foreach (var resident in roster)
            {
                if (!byCode.TryGetValue(resident.Room, out var room))
                    throw new InvalidOperationException($"ELTS roster row {resident.SourceRow} has an invalid room code: {resident.Room}.");
                room.Residents.Add(new DormResident
                {
                    FirstName = resident.FirstName,
                    LastName = resident.LastName,
                    SportOrActivity = resident.Activity,
                    SourceRow = resident.SourceRow
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SchedulePeriod CreatePeriod(ResidenceHall hall, int year, int month, ScheduleStatus status)
    {
        var period = new SchedulePeriod
        {
            ResidenceHall = hall, Year = year, Month = month, RequiredStaffPerShift = 1,
            MaximumShiftsPerUser = 6, MaximumWeekendShiftsPerUser = 3,
            AllowConsecutiveShifts = false, AllowSelfRemovalAfterClose = false, RequiresApproval = false
        };
        period.SetInitialStatusForSeed(status);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(hall.TimeZone);
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var date = new DateOnly(year, month, day);
            var times = ShiftTimeFactory.Create(date, zone);
            period.Shifts.Add(new DutyShift { DutyDate = date, StartsAt = times.StartsAt, EndsAt = times.EndsAt, RequiredStaffCount = 1 });
        }
        return period;
    }

    private static User User(string id, string oid, string first, string last, string email, string room, string phone,
        HallRole role = HallRole.ResidentAssistant) => new()
    {
        Id = Guid.Parse(id), EntraObjectId = oid, FirstName = first, LastName = last,
        SchoolEmail = email, RoomNumber = room, PhoneNumber = phone, Role = role
    };

    private sealed record ResidentSeed(int SourceRow, string LastName, string FirstName, string Room, string? Activity);
}
