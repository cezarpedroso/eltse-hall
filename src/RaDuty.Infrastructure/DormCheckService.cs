using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormCheckService(RaDutyDbContext db, ICurrentUserService currentUserService, IConfiguration? configuration = null) : IDormCheckService
{
    private readonly string photoStorageRoot = ResolvePhotoStorageRoot(configuration);

    public async Task<IReadOnlyList<DormSuiteDto>> GetSuitesAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var rooms = await LoadRoomsAsync(current.ResidenceHallId, cancellationToken);

        return rooms.GroupBy(x => x.SuiteNumber)
            .Select(suite => new DormSuiteDto(suite.Key, suite.Select(room =>
            {
                var latest = room.Checks.OrderByDescending(x => x.CheckedAt).FirstOrDefault();
                return new DormRoomDto(room.Id, RoomCode(room), room.RoomLetter,
                    room.Residents.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
                        .Select(x => new DormResidentDto(x.Id, x.FirstName, x.LastName)).ToList(),
                    latest is null ? null : new DormRoomCheckSummaryDto(latest.Id, latest.CheckedByUserId,
                        $"{latest.CheckedByUser.FirstName} {latest.CheckedByUser.LastName}", latest.CheckedAt, latest.Photos.Count));
            }).ToList())).ToList();
    }

    public async Task<DormCheckReportDto> GetReportAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var rooms = await LoadRoomsAsync(current.ResidenceHallId, cancellationToken);
        var suites = rooms.GroupBy(x => x.SuiteNumber).Select(suite => new DormSuiteReportDto(suite.Key,
            suite.Select(room =>
            {
                var latest = room.Checks.OrderByDescending(x => x.CheckedAt).FirstOrDefault();
                return new DormRoomReportDto(room.Id, RoomCode(room), room.RoomLetter,
                    room.Residents.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
                        .Select(x => new DormResidentDto(x.Id, x.FirstName, x.LastName)).ToList(),
                    latest is null ? null : ToDto(latest, room));
            }).ToList())).ToList();
        return new DormCheckReportDto(current.ResidenceHallName, suites);
    }

    public async Task<DormRoomCheckDto> SubmitAsync(Guid roomId, SubmitDormRoomCheckRequest request, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var room = await db.DormRooms.SingleOrDefaultAsync(x => x.Id == roomId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_ROOM_NOT_FOUND", "Dorm room not found in your hall.");
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > 2000) throw new AppException(400, "NOTES_TOO_LONG", "Notes must be 2,000 characters or fewer.");

        var check = new DormRoomCheck
        {
            DormRoomId = room.Id,
            CheckedByUserId = current.Id,
            IsRoomClean = request.IsRoomClean,
            IsAllFurniturePresent = request.IsAllFurniturePresent,
            IsSmokeDetectorClear = request.IsSmokeDetectorClear,
            IsRoomOdorFree = request.IsRoomOdorFree,
            IsRoomTrashFree = request.IsRoomTrashFree,
            IsCommonAreaClean = request.IsCommonAreaClean,
            IsRoomAlcoholFree = request.IsRoomAlcoholFree,
            IsRoomDamageFree = request.IsRoomDamageFree,
            Notes = notes
        };
        db.DormRoomChecks.Add(check);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "DORM_ROOM_CHECK_COMPLETED",
            EntityType = "DormRoom",
            EntityId = room.Id.ToString(),
            NewValuesJson = JsonSerializer.Serialize(new { Room = RoomCode(room), check.CheckedAt })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new DormRoomCheckDto(check.Id, room.Id, RoomCode(room), current.Id,
            $"{current.FirstName} {current.LastName}", check.CheckedAt, check.IsRoomClean,
            check.IsAllFurniturePresent, check.IsSmokeDetectorClear, check.IsRoomOdorFree,
            check.IsRoomTrashFree, check.IsCommonAreaClean, check.IsRoomAlcoholFree,
            check.IsRoomDamageFree, check.Notes);
    }

    public async Task<DormCheckResetDto> ResetAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var checks = await db.DormRoomChecks
            .Where(check => check.DormRoom.ResidenceHallId == current.ResidenceHallId)
            .ToListAsync(cancellationToken);
        if (checks.Count == 0) return new DormCheckResetDto(0, 0);

        var checkIds = checks.Select(check => check.Id).ToList();
        var storedPhotos = await db.DormCheckPhotos
            .Where(photo => checkIds.Contains(photo.DormRoomCheckId))
            .Select(photo => photo.StoredFileName)
            .ToListAsync(cancellationToken);
        db.DormRoomChecks.RemoveRange(checks);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "DORM_CHECKS_RESET",
            EntityType = "ResidenceHall",
            EntityId = current.ResidenceHallId.ToString(),
            OldValuesJson = JsonSerializer.Serialize(new { CheckCount = checks.Count, PhotoCount = storedPhotos.Count })
        });
        await db.SaveChangesAsync(cancellationToken);

        foreach (var storedPhoto in storedPhotos) DeleteStoredPhoto(storedPhoto);
        return new DormCheckResetDto(checks.Count, storedPhotos.Count);
    }

    private static string RoomCode(DormRoom room) => $"ELTS-{room.SuiteNumber}{room.RoomLetter}";

    private static string ResolvePhotoStorageRoot(IConfiguration? configuration)
    {
        var configured = configuration?["DormCheckPhotos:StoragePath"] ?? "App_Data/DormCheckPhotos";
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(Directory.GetCurrentDirectory(), configured));
    }

    private void DeleteStoredPhoto(string storedFileName)
    {
        if (!string.Equals(storedFileName, Path.GetFileName(storedFileName), StringComparison.Ordinal)) return;
        var path = Path.GetFullPath(Path.Combine(photoStorageRoot, storedFileName));
        if (!path.StartsWith(photoStorageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Task<List<DormRoom>> LoadRoomsAsync(Guid residenceHallId, CancellationToken cancellationToken) =>
        db.DormRooms.AsNoTracking()
            .Include(x => x.Residents)
            .Include(x => x.Checks.OrderByDescending(check => check.CheckedAt).Take(1)).ThenInclude(x => x.CheckedByUser)
            .Include(x => x.Checks.OrderByDescending(check => check.CheckedAt).Take(1)).ThenInclude(x => x.Photos)
            .Where(x => x.ResidenceHallId == residenceHallId)
            .OrderBy(x => x.SuiteNumber).ThenBy(x => x.RoomLetter)
            .ToListAsync(cancellationToken);

    private static DormRoomCheckDto ToDto(DormRoomCheck check, DormRoom room) => new(check.Id, room.Id,
        RoomCode(room), check.CheckedByUserId, $"{check.CheckedByUser.FirstName} {check.CheckedByUser.LastName}",
        check.CheckedAt, check.IsRoomClean, check.IsAllFurniturePresent, check.IsSmokeDetectorClear,
        check.IsRoomOdorFree, check.IsRoomTrashFree, check.IsCommonAreaClean, check.IsRoomAlcoholFree,
        check.IsRoomDamageFree, check.Notes, check.Photos.Count);
}
