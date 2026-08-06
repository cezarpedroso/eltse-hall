using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class CurrentUserService(RaDutyDbContext db, ICurrentIdentityAccessor identityAccessor) : ICurrentUserService
{
    public async Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken)
    {
        var identity = identityAccessor.GetRequired();
        var user = await db.Users.Include(x => x.HallMemberships).ThenInclude(x => x.ResidenceHall)
            .SingleOrDefaultAsync(x => x.EntraObjectId == identity.EntraObjectId, cancellationToken);

        if (user is null)
        {
            var hall = await db.ResidenceHalls.FirstOrDefaultAsync(x => x.IsActive, cancellationToken)
                ?? throw new AppException(403, "NO_ACTIVE_HALL", "No active residence hall is configured.");
            user = new User
            {
                EntraObjectId = identity.EntraObjectId,
                SchoolEmail = identity.Email,
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Role = TrustedRole(identity)
            };
            user.HallMemberships.Add(new HallMembership
            {
                ResidenceHall = hall,
                User = user,
                HallRole = user.Role
            });
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!user.IsActive) throw new AppException(403, "USER_INACTIVE", "Your residence-life account is inactive.");
        var membership = user.HallMemberships.SingleOrDefault(x => x.IsActive && x.ResidenceHall.IsActive)
            ?? throw new AppException(403, "NO_ACTIVE_MEMBERSHIP", "You do not have an active hall membership.");
        var trustedRole = TrustedRole(identity);
        if (user.Role != trustedRole || membership.HallRole != trustedRole)
        {
            user.Role = trustedRole;
            membership.HallRole = trustedRole;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return ToDto(user, membership);
    }

    internal static CurrentUserDto ToDto(User user, HallMembership membership) => new(user.Id, user.EntraObjectId,
        user.SchoolEmail, user.FirstName, user.LastName, user.RoomNumber, user.PhoneNumber, membership.HallRole,
        user.IsActive, membership.ResidenceHallId, membership.ResidenceHall.Name);

    private static HallRole TrustedRole(CurrentIdentity identity) => identity.IsAdmin ? HallRole.Admin
        : identity.IsHallDirector ? HallRole.HallDirector : HallRole.ResidentAssistant;
}

public sealed class UserService(RaDutyDbContext db, ICurrentUserService currentUserService) : IUserService
{
    public async Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ValidateContact(request.RoomNumber, request.PhoneNumber);
        var current = await currentUserService.GetAsync(cancellationToken);
        var user = await db.Users.Include(x => x.HallMemberships).ThenInclude(x => x.ResidenceHall)
            .SingleAsync(x => x.Id == current.Id, cancellationToken);
        user.RoomNumber = Clean(request.RoomNumber);
        user.PhoneNumber = Clean(request.PhoneNumber);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return CurrentUserService.ToDto(user, user.HallMemberships.Single(x => x.IsActive));
    }

    public async Task<IReadOnlyList<ShiftDto>> GetMyShiftsAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var shifts = await db.DutyShifts.AsNoTracking()
            .Include(x => x.Assignments.Where(a => a.RemovedAt == null)).ThenInclude(x => x.User)
            .Where(x => x.EndsAt >= DateTimeOffset.UtcNow && x.Assignments.Any(a => a.UserId == current.Id && a.RemovedAt == null))
            .OrderBy(x => x.StartsAt).ToListAsync(cancellationToken);
        return shifts.Select(x => x.ToDto(current.Id)).ToList();
    }

    public async Task<IReadOnlyList<ResidentAssistantDto>> GetDirectoryAsync(string? search, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var query = db.HallMemberships.AsNoTracking().Include(x => x.User)
            .Where(x => x.ResidenceHallId == current.ResidenceHallId && x.IsActive && x.User.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.User.FirstName.Contains(term) || x.User.LastName.Contains(term) || x.User.RoomNumber!.Contains(term));
        }
        return await query.OrderBy(x => x.User.LastName).ThenBy(x => x.User.FirstName)
            .Select(x => new ResidentAssistantDto(x.User.Id, x.User.FirstName, x.User.LastName, x.User.SchoolEmail,
                x.User.RoomNumber, x.User.PhoneNumber, x.HallRole, x.User.IsActive)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResidentAssistantDto>> GetUsersAsync(string? search, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var hall = await db.ResidenceHalls.AsNoTracking().SingleAsync(x => x.Id == current.ResidenceHallId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(hall.TimeZone));
        var periodId = await db.SchedulePeriods.Where(x => x.ResidenceHallId == current.ResidenceHallId && x.Year == localNow.Year && x.Month == localNow.Month)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var query = db.HallMemberships.AsNoTracking().Include(x => x.User).Where(x => x.ResidenceHallId == current.ResidenceHallId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.User.FirstName.Contains(term) || x.User.LastName.Contains(term) || x.User.RoomNumber!.Contains(term));
        }
        var memberships = await query.OrderBy(x => x.User.LastName).ThenBy(x => x.User.FirstName).ToListAsync(cancellationToken);
        Dictionary<Guid, int> counts = periodId.HasValue
            ? await db.ShiftAssignments.AsNoTracking().Where(x => x.RemovedAt == null && x.DutyShift.SchedulePeriodId == periodId.Value)
                .GroupBy(x => x.UserId).Select(x => new { UserId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken)
            : [];
        return memberships.Select(x => new ResidentAssistantDto(x.User.Id, x.User.FirstName, x.User.LastName, x.User.SchoolEmail,
            x.User.RoomNumber, x.User.PhoneNumber, x.HallRole, x.User.IsActive && x.IsActive, counts.GetValueOrDefault(x.UserId))).ToList();
    }

    public async Task<ResidentAssistantDto> GetResidentAssistantAsync(Guid id, CancellationToken cancellationToken)
    {
        var users = await GetDirectoryAsync(null, cancellationToken);
        return users.SingleOrDefault(x => x.Id == id) ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found.");
    }

    public async Task<ResidentAssistantDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ValidateContact(request.RoomNumber, request.PhoneNumber);
        var actor = await currentUserService.GetAsync(cancellationToken);
        var user = await db.Users.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found.");
        var membership = user.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == actor.ResidenceHallId)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found in this hall.");
        var before = new { user.RoomNumber, user.IsActive };
        user.RoomNumber = Clean(request.RoomNumber);
        user.PhoneNumber = Clean(request.PhoneNumber);
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        membership.IsActive = request.IsActive;
        db.AuditLogs.Add(Audit(actor.Id, "USER_UPDATED", "User", user.Id, before,
            new { user.RoomNumber, user.IsActive }));
        await db.SaveChangesAsync(cancellationToken);
        return new ResidentAssistantDto(user.Id, user.FirstName, user.LastName, user.SchoolEmail,
            user.RoomNumber, user.PhoneNumber, membership.HallRole, user.IsActive);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateContact(string? room, string? phone)
    {
        if (room?.Length > 30) throw new AppException(400, "INVALID_ROOM_NUMBER", "Room number must be 30 characters or fewer.");
        if (phone?.Length > 30 || phone is not null && phone.Any(c => !char.IsDigit(c) && c is not '+' and not '-' and not '(' and not ')' and not ' '))
            throw new AppException(400, "INVALID_PHONE_NUMBER", "Enter a valid phone number with 30 characters or fewer.");
    }

    private static AuditLog Audit(Guid actor, string action, string type, Guid id, object before, object after) => new()
    {
        ActorUserId = actor, Action = action, EntityType = type, EntityId = id.ToString(),
        OldValuesJson = System.Text.Json.JsonSerializer.Serialize(before),
        NewValuesJson = System.Text.Json.JsonSerializer.Serialize(after)
    };
}
