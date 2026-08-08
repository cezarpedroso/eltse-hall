using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public static class DevelopmentSeed
{
    private static readonly Guid HallId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RetiredSeedUserId = Guid.Parse("20000000-0000-0000-0000-000000000009");
    private const string HallName = "Eltse Hall";
    private static readonly StaffSeed[] CurrentStaff =
    [
        new("20000000-0000-0000-0000-000000000001", "Carol", "Ocker", "carol.ocker@wmpenn.edu", HallRole.HallDirector),
        new("20000000-0000-0000-0000-000000000002", "Jennie", "Robison", "jennierobison@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000003", "Drake", "Hamm", "drakehamm@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000004", "Lillian", "Zapata", "lillianzapata@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000005", "Madelynn", "Zehr", "madelynnzehr@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000006", "Madison", "Gustafson", "madisongustafson@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000007", "Gavin", "Huff", "gavinhuff@wmpenn.edu", HallRole.ResidentAssistant),
        new("20000000-0000-0000-0000-000000000008", "Cezar", "Pedroso", "cezarpedroso@wmpenn.edu", HallRole.Admin)
    ];

    public static async Task InitializeAsync(RaDutyDbContext db, IPasswordHasher<ApplicationAccount> passwordHasher,
        string initialPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(initialPassword) || initialPassword.Length < 8)
            throw new InvalidOperationException("DevelopmentAccounts:InitialPassword must contain at least 8 characters.");
        var existingHall = await db.ResidenceHalls.SingleOrDefaultAsync(x => x.Id == HallId, cancellationToken);
        if (existingHall is not null)
        {
            if (existingHall.Name != HallName)
            {
                existingHall.Name = HallName;
                await db.SaveChangesAsync(cancellationToken);
            }
            await SynchronizeDevelopmentStaffAsync(db, existingHall, passwordHasher, initialPassword, cancellationToken);
            await EnsureDormRosterAsync(db, existingHall, cancellationToken);
            return;
        }
        if (await db.ResidenceHalls.AnyAsync(cancellationToken)) return;

        var hall = new ResidenceHall { Id = HallId, Name = HallName, TimeZone = "America/Chicago" };
        var users = CurrentStaff.Select(User).ToArray();
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
            var user = users[1 + index % (users.Length - 1)];
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
            var user = users[1 + index % (users.Length - 1)];
            shift.Assignments.Add(new ShiftAssignment { DutyShift = shift, User = user, AssignedByUserId = users[0].Id });
            shift.Status = ShiftStatus.Full;
        }
        db.AddRange(current, previous);
        await db.SaveChangesAsync(cancellationToken);
        await EnsureDormRosterAsync(db, hall, cancellationToken);
        await EnsureDevelopmentAccountsAsync(db, passwordHasher, initialPassword, cancellationToken);
    }

    private static async Task SynchronizeDevelopmentStaffAsync(RaDutyDbContext db, ResidenceHall hall,
        IPasswordHasher<ApplicationAccount> passwordHasher, string initialPassword, CancellationToken cancellationToken)
    {
        var rosterIds = CurrentStaff.Select(x => Guid.Parse(x.Id)).ToArray();
        var users = await db.StaffUsers.Include(x => x.HallMemberships)
            .Where(x => rosterIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var seed in CurrentStaff)
        {
            var id = Guid.Parse(seed.Id);
            if (!users.TryGetValue(id, out var user))
            {
                user = User(seed);
                user.HallMemberships.Add(new HallMembership { ResidenceHall = hall, User = user, HallRole = seed.Role });
                db.StaffUsers.Add(user);
                users.Add(id, user);
            }
            else
            {
                user.FirstName = seed.FirstName;
                user.LastName = seed.LastName;
                user.SchoolEmail = seed.Email;
                user.RoomNumber = null;
                user.PhoneNumber = null;
                user.Role = seed.Role;
                user.IsActive = true;
                user.UpdatedAt = DateTimeOffset.UtcNow;
                var membership = user.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == HallId);
                if (membership is null)
                    user.HallMemberships.Add(new HallMembership { ResidenceHall = hall, User = user, HallRole = seed.Role });
                else
                {
                    membership.HallRole = seed.Role;
                    membership.IsActive = true;
                }
            }
        }

        var retired = await db.StaffUsers.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.Id == RetiredSeedUserId, cancellationToken);
        if (retired is not null && retired.IsActive)
        {
            retired.IsActive = false;
            foreach (var membership in retired.HallMemberships) membership.IsActive = false;
            var retiredAccount = await db.Users.SingleOrDefaultAsync(x => x.UserId == retired.Id, cancellationToken);
            if (retiredAccount is not null) retiredAccount.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        await db.SaveChangesAsync(cancellationToken);
        await EnsureDevelopmentAccountsAsync(db, passwordHasher, initialPassword, cancellationToken);
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

    private static User User(StaffSeed seed) => new()
    {
        Id = Guid.Parse(seed.Id), FirstName = seed.FirstName, LastName = seed.LastName,
        SchoolEmail = seed.Email, Role = seed.Role
    };

    private static async Task EnsureDevelopmentAccountsAsync(RaDutyDbContext db,
        IPasswordHasher<ApplicationAccount> passwordHasher, string initialPassword, CancellationToken cancellationToken)
    {
        var currentIds = CurrentStaff.Select(x => Guid.Parse(x.Id)).ToArray();
        var users = await db.StaffUsers.Where(x => currentIds.Contains(x.Id)
                && x.HallMemberships.Any(m => m.ResidenceHallId == HallId))
            .ToListAsync(cancellationToken);
        var userIds = users.Select(x => x.Id).ToArray();
        var accounts = await db.Users.Where(x => userIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        foreach (var user in users)
        {
            var normalizedEmail = user.SchoolEmail.ToUpperInvariant();
            if (accounts.TryGetValue(user.Id, out var existing))
            {
                if (!string.Equals(existing.Email, user.SchoolEmail, StringComparison.OrdinalIgnoreCase))
                {
                    existing.UserName = user.SchoolEmail;
                    existing.NormalizedUserName = normalizedEmail;
                    existing.Email = user.SchoolEmail;
                    existing.NormalizedEmail = normalizedEmail;
                    existing.PasswordHash = passwordHasher.HashPassword(existing, initialPassword);
                    existing.MustChangePassword = true;
                    existing.PasswordChangedAt = DateTimeOffset.UtcNow;
                    existing.AccessFailedCount = 0;
                    existing.LockoutEnd = null;
                    existing.SecurityStamp = Guid.NewGuid().ToString("N");
                }
                continue;
            }
            var account = new ApplicationAccount
            {
                Id = user.Id,
                UserId = user.Id,
                UserName = user.SchoolEmail,
                NormalizedUserName = normalizedEmail,
                Email = user.SchoolEmail,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                LockoutEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                MustChangePassword = true,
                PasswordChangedAt = DateTimeOffset.UtcNow
            };
            account.PasswordHash = passwordHasher.HashPassword(account, initialPassword);
            db.Users.Add(account);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record StaffSeed(string Id, string FirstName, string LastName, string Email, HallRole Role);
    private sealed record ResidentSeed(int SourceRow, string LastName, string FirstName, string Room, string? Activity);
}
