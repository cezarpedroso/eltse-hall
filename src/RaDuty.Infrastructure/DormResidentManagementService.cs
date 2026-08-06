using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormResidentManagementService(RaDutyDbContext db, ICurrentUserService currentUserService) : IDormResidentManagementService
{
    private const int RoomCapacity = 2;

    public async Task<IReadOnlyList<ManagedDormResidentDto>> GetAsync(string? search, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var query = db.DormResidents.AsNoTracking().Include(resident => resident.DormRoom)
            .Where(resident => resident.DormRoom.ResidenceHallId == current.ResidenceHallId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(resident => resident.FirstName.Contains(term) || resident.LastName.Contains(term) ||
                resident.DormRoom.SuiteNumber.Contains(term) || resident.DormRoom.RoomLetter.Contains(term));
        }
        return await query.OrderBy(resident => resident.DormRoom.SuiteNumber).ThenBy(resident => resident.DormRoom.RoomLetter)
            .ThenBy(resident => resident.LastName).ThenBy(resident => resident.FirstName)
            .Select(resident => new ManagedDormResidentDto(resident.Id, resident.FirstName, resident.LastName,
                resident.DormRoomId, $"ELTS-{resident.DormRoom.SuiteNumber}{resident.DormRoom.RoomLetter}", resident.SportOrActivity))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DormRoomOptionDto>> GetRoomsAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        return await db.DormRooms.AsNoTracking().Where(room => room.ResidenceHallId == current.ResidenceHallId)
            .OrderBy(room => room.SuiteNumber).ThenBy(room => room.RoomLetter)
            .Select(room => new DormRoomOptionDto(room.Id, $"ELTS-{room.SuiteNumber}{room.RoomLetter}", room.Residents.Count, RoomCapacity))
            .ToListAsync(cancellationToken);
    }

    public async Task<ManagedDormResidentDto> CreateAsync(SaveDormResidentRequest request, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var values = Validate(request);
        var room = await GetRoomAsync(request.DormRoomId, current.ResidenceHallId, cancellationToken);
        if (room.Residents.Count >= RoomCapacity)
            throw new AppException(409, "DORM_ROOM_AT_CAPACITY", $"{RoomCode(room)} already has {RoomCapacity} residents.");
        await EnsureUniqueAsync(values.FirstName, values.LastName, current.ResidenceHallId, null, cancellationToken);

        var resident = new DormResident
        {
            DormRoomId = room.Id,
            FirstName = values.FirstName,
            LastName = values.LastName,
            SportOrActivity = values.Activity
        };
        db.DormResidents.Add(resident);
        db.AuditLogs.Add(Audit(current.Id, "DORM_RESIDENT_ADDED", resident.Id, null,
            new { resident.FirstName, resident.LastName, Room = RoomCode(room), resident.SportOrActivity }));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(resident, room);
    }

    public async Task<ManagedDormResidentDto> UpdateAsync(Guid residentId, SaveDormResidentRequest request, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var values = Validate(request);
        var resident = await db.DormResidents.Include(item => item.DormRoom)
            .SingleOrDefaultAsync(item => item.Id == residentId && item.DormRoom.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_RESIDENT_NOT_FOUND", "Resident not found in Eltse Hall.");
        var room = await GetRoomAsync(request.DormRoomId, current.ResidenceHallId, cancellationToken);
        if (resident.DormRoomId != room.Id && room.Residents.Count >= RoomCapacity)
            throw new AppException(409, "DORM_ROOM_AT_CAPACITY", $"{RoomCode(room)} already has {RoomCapacity} residents.");
        await EnsureUniqueAsync(values.FirstName, values.LastName, current.ResidenceHallId, resident.Id, cancellationToken);

        var before = new { resident.FirstName, resident.LastName, Room = RoomCode(resident.DormRoom), resident.SportOrActivity };
        resident.FirstName = values.FirstName;
        resident.LastName = values.LastName;
        resident.DormRoomId = room.Id;
        resident.SportOrActivity = values.Activity;
        resident.SourceRow = null;
        db.AuditLogs.Add(Audit(current.Id, "DORM_RESIDENT_UPDATED", resident.Id, before,
            new { resident.FirstName, resident.LastName, Room = RoomCode(room), resident.SportOrActivity }));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(resident, room);
    }

    public async Task RemoveAsync(Guid residentId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var resident = await db.DormResidents.Include(item => item.DormRoom)
            .SingleOrDefaultAsync(item => item.Id == residentId && item.DormRoom.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_RESIDENT_NOT_FOUND", "Resident not found in Eltse Hall.");
        db.AuditLogs.Add(Audit(current.Id, "DORM_RESIDENT_TRANSFERRED_OUT", resident.Id,
            new { resident.FirstName, resident.LastName, Room = RoomCode(resident.DormRoom), resident.SportOrActivity }, null));
        db.DormResidents.Remove(resident);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DormRoom> GetRoomAsync(Guid roomId, Guid hallId, CancellationToken cancellationToken) =>
        await db.DormRooms.Include(room => room.Residents)
            .SingleOrDefaultAsync(room => room.Id == roomId && room.ResidenceHallId == hallId, cancellationToken)
        ?? throw new AppException(404, "DORM_ROOM_NOT_FOUND", "The selected room was not found in Eltse Hall.");

    private async Task EnsureUniqueAsync(string firstName, string lastName, Guid hallId, Guid? exceptId, CancellationToken cancellationToken)
    {
        var residents = await db.DormResidents.AsNoTracking().Where(resident => resident.DormRoom.ResidenceHallId == hallId && resident.Id != exceptId)
            .Select(resident => new { resident.FirstName, resident.LastName }).ToListAsync(cancellationToken);
        if (residents.Any(resident => string.Equals(resident.FirstName.Trim(), firstName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(resident.LastName.Trim(), lastName, StringComparison.OrdinalIgnoreCase)))
            throw new AppException(409, "DORM_RESIDENT_DUPLICATE", $"{firstName} {lastName} is already listed in Eltse Hall.");
    }

    private static (string FirstName, string LastName, string? Activity) Validate(SaveDormResidentRequest request)
    {
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var activity = string.IsNullOrWhiteSpace(request.SportOrActivity) ? null : request.SportOrActivity.Trim();
        if (firstName.Length == 0 || lastName.Length == 0)
            throw new AppException(400, "DORM_RESIDENT_NAME_REQUIRED", "Enter the resident's first and last name.");
        if (firstName.Length > 80 || lastName.Length > 80)
            throw new AppException(400, "DORM_RESIDENT_NAME_TOO_LONG", "Resident names must be 80 characters or fewer.");
        if (activity?.Length > 120)
            throw new AppException(400, "DORM_RESIDENT_ACTIVITY_TOO_LONG", "Sport or activity must be 120 characters or fewer.");
        return (firstName, lastName, activity);
    }

    private static ManagedDormResidentDto ToDto(DormResident resident, DormRoom room) => new(resident.Id,
        resident.FirstName, resident.LastName, room.Id, RoomCode(room), resident.SportOrActivity);
    private static string RoomCode(DormRoom room) => $"ELTS-{room.SuiteNumber}{room.RoomLetter}";
    private static AuditLog Audit(Guid actor, string action, Guid residentId, object? before, object? after) => new()
    {
        ActorUserId = actor,
        Action = action,
        EntityType = "DormResident",
        EntityId = residentId.ToString(),
        OldValuesJson = before is null ? null : JsonSerializer.Serialize(before),
        NewValuesJson = after is null ? null : JsonSerializer.Serialize(after)
    };
}
