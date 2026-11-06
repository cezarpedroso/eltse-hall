using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class CurrentUserService(RaDutyDbContext db, ICurrentIdentityAccessor identityAccessor,
    IHttpContextAccessor? httpContextAccessor = null) : ICurrentUserService
{
    public async Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken)
    {
        var identity = identityAccessor.GetRequired();
        var user = await db.StaffUsers.Include(x => x.HallMemberships).ThenInclude(x => x.ResidenceHall)
            .SingleOrDefaultAsync(x => x.Id == identity.UserId, cancellationToken)
            ?? throw new AppException(401, "ACCOUNT_NOT_FOUND", "Your account is no longer available.");

        if (!user.IsActive) throw new AppException(403, "USER_INACTIVE", "Your residence-life account is inactive.");
        var membership = user.HallMemberships.SingleOrDefault(x => x.IsActive && x.ResidenceHall.IsActive)
            ?? throw new AppException(403, "NO_ACTIVE_MEMBERSHIP", "You do not have an active hall membership.");
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken)
            ?? throw new AppException(401, "ACCOUNT_NOT_FOUND", "Your account is no longer available.");
        var current = ToDto(user, membership, account.MustChangePassword);
        if (!IsCampusAdministrator(current.Role)
            || !Guid.TryParse(httpContextAccessor?.HttpContext?.Request.Headers["X-Active-Residence-Hall"], out var requestedHallId)
            || requestedHallId == current.ResidenceHallId)
            return current;

        var requestedHall = await db.ResidenceHalls.AsNoTracking()
            .SingleOrDefaultAsync(hall => hall.Id == requestedHallId && hall.IsActive, cancellationToken)
            ?? throw new AppException(400, "INVALID_RESIDENCE_HALL", "Choose an active residence hall.");
        return current with { ResidenceHallId = requestedHall.Id, ResidenceHallName = requestedHall.Name };
    }

    internal static CurrentUserDto ToDto(User user, HallMembership membership, bool mustChangePassword = false) => new(user.Id,
        user.SchoolEmail, user.FirstName, user.LastName, user.RoomNumber, user.PhoneNumber, membership.HallRole,
        user.IsActive, membership.ResidenceHallId, membership.ResidenceHall.Name, mustChangePassword);

    private static bool IsCampusAdministrator(HallRole role) => role is HallRole.Admin or HallRole.DeanOfStudents or HallRole.AssistantDirectorOfResidenceLife;
}

public sealed class UserService(RaDutyDbContext db, ICurrentUserService currentUserService,
    UserManager<ApplicationAccount> accountManager) : IUserService
{
    public async Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ValidateContact(null, request.PhoneNumber);
        var current = await currentUserService.GetAsync(cancellationToken);
        var user = await db.StaffUsers.Include(x => x.HallMemberships).ThenInclude(x => x.ResidenceHall)
            .SingleAsync(x => x.Id == current.Id, cancellationToken);
        user.PhoneNumber = Clean(request.PhoneNumber);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return CurrentUserService.ToDto(user, user.HallMemberships.Single(x => x.IsActive)) with
        {
            ResidenceHallId = current.ResidenceHallId,
            ResidenceHallName = current.ResidenceHallName
        };
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
        var query = db.HallMemberships.AsNoTracking().Include(x => x.User).Include(x => x.ResidenceHall)
            .Where(x => x.ResidenceHallId == current.ResidenceHallId && x.IsActive);
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
            x.User.RoomNumber, x.User.PhoneNumber, x.HallRole, x.User.IsActive && x.IsActive, counts.GetValueOrDefault(x.UserId),
            x.ResidenceHallId, x.ResidenceHall.Name)).ToList();
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
        var user = await db.StaffUsers.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found.");
        if (!IsCampusAdministrator(actor.Role))
            throw new AppException(403, "ACCOUNT_MANAGEMENT_NOT_ALLOWED", "Only campus administrators can manage accounts.");
        if (!Enum.IsDefined(request.Role))
            throw new AppException(400, "INVALID_ROLE", "Choose a valid residence-life role.");
        var membership = user.HallMemberships.SingleOrDefault(x => x.IsActive)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found in this hall.");
        var requestedHallId = request.ResidenceHallId ?? membership.ResidenceHallId;
        if (!await db.ResidenceHalls.AnyAsync(hall => hall.Id == requestedHallId && hall.IsActive, cancellationToken))
            throw new AppException(400, "INVALID_RESIDENCE_HALL", "Choose an active residence hall.");
        if (actor.Id == user.Id && (!request.IsActive || request.Role != actor.Role || requestedHallId != actor.ResidenceHallId))
            throw new AppException(400, "CANNOT_REMOVE_OWN_ACCESS", "You cannot change, deactivate, or move your own access.");

        var before = new { user.RoomNumber, user.PhoneNumber, user.Role, user.IsActive, membership.ResidenceHallId };
        var invalidatesSession = user.Role != request.Role || user.IsActive != request.IsActive || membership.ResidenceHallId != requestedHallId;
        user.RoomNumber = Clean(request.RoomNumber);
        user.PhoneNumber = Clean(request.PhoneNumber);
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        if (membership.ResidenceHallId != requestedHallId)
        {
            membership.IsActive = false;
            membership = user.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == requestedHallId)
                ?? new HallMembership { ResidenceHallId = requestedHallId, User = user };
            if (!user.HallMemberships.Contains(membership)) user.HallMemberships.Add(membership);
        }
        membership.HallRole = request.Role;
        membership.IsActive = request.IsActive;
        var account = await accountManager.FindByIdAsync(user.Id.ToString())
            ?? throw new AppException(409, "ACCOUNT_NOT_PROVISIONED", "This person does not have a login account.");
        if (invalidatesSession) await accountManager.UpdateSecurityStampAsync(account);
        db.AuditLogs.Add(Audit(actor.Id, "USER_UPDATED", "User", user.Id, before,
            new { user.RoomNumber, user.PhoneNumber, user.Role, user.IsActive, membership.ResidenceHallId }));
        await db.SaveChangesAsync(cancellationToken);
        var hallName = await db.ResidenceHalls.Where(hall => hall.Id == membership.ResidenceHallId)
            .Select(hall => hall.Name).SingleAsync(cancellationToken);
        return new ResidentAssistantDto(user.Id, user.FirstName, user.LastName, user.SchoolEmail,
            user.RoomNumber, user.PhoneNumber, membership.HallRole, user.IsActive, 0, membership.ResidenceHallId, hallName);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetAsync(cancellationToken);
        var user = await db.StaffUsers.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found.");
        if (!IsCampusAdministrator(actor.Role))
            throw new AppException(403, "ACCOUNT_MANAGEMENT_NOT_ALLOWED", "Only campus administrators can manage accounts.");
        var membership = user.HallMemberships.SingleOrDefault(x => x.IsActive)
            ?? throw new AppException(404, "USER_NOT_FOUND", "Resident assistant not found in this hall.");
        if (actor.Id == user.Id)
            throw new AppException(400, "CANNOT_REMOVE_OWN_ACCESS", "You cannot delete your own account.");

        var before = new { user.RoomNumber, user.PhoneNumber, user.Role, user.IsActive, membership.HallRole, MembershipActive = membership.IsActive };
        user.IsActive = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var account = await accountManager.FindByIdAsync(user.Id.ToString());
        if (account is not null)
        {
            var result = await accountManager.DeleteAsync(account);
            if (!result.Succeeded)
                throw new AppException(409, "ACCOUNT_DELETE_FAILED", "The login account could not be deleted.");
            db.Entry(account).State = EntityState.Detached;
        }
        db.HallMemberships.Remove(membership);
        db.AuditLogs.Add(Audit(actor.Id, "USER_DELETED", "User", user.Id, before,
            new { user.Role, user.IsActive, HallMembershipRemoved = true, LoginDeleted = account is not null }));
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsCampusAdministrator(HallRole role) => role is HallRole.Admin or HallRole.DeanOfStudents or HallRole.AssistantDirectorOfResidenceLife;

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
