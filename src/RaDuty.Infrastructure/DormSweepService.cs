using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormSweepService(RaDutyDbContext db, ICurrentUserService currentUserService, IConfiguration? configuration = null) : IDormSweepService
{
    private const int SuiteCount = 25;
    private readonly string photoStorageRoot = ResolvePhotoStorageRoot(configuration);

    public async Task<IReadOnlyList<DormSweepSuiteDto>> GetSuitesAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var latestBySuite = await LoadLatestSweepsAsync(current.ResidenceHallId, cancellationToken);

        return Enumerable.Range(1, SuiteCount)
            .Select(number => number.ToString("00"))
            .Select(suiteNumber => new DormSweepSuiteDto(suiteNumber,
                latestBySuite.TryGetValue(suiteNumber, out var sweep) ? ToSummaryDto(sweep) : null,
                sweep is null ? null : ToDto(sweep)))
            .ToList();
    }

    public async Task<DormSweepReportDto> GetReportAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var latestBySuite = await LoadLatestSweepsAsync(current.ResidenceHallId, cancellationToken);
        var residents = await db.DormRooms.AsNoTracking()
            .Include(x => x.Residents)
            .Where(x => x.ResidenceHallId == current.ResidenceHallId)
            .OrderBy(x => x.SuiteNumber).ThenBy(x => x.RoomLetter)
            .ToListAsync(cancellationToken);
        var residentsBySuite = residents.GroupBy(x => x.SuiteNumber).ToDictionary(x => x.Key,
            suite => suite.SelectMany(room => room.Residents
                .OrderBy(resident => resident.LastName).ThenBy(resident => resident.FirstName)
                .Select(resident => new DormSweepResidentDto(resident.Id, resident.FirstName, resident.LastName, RoomCode(room))))
                .ToList());

        var suites = Enumerable.Range(1, SuiteCount)
            .Select(number => number.ToString("00"))
            .Select(suiteNumber => new DormSweepSuiteReportDto(suiteNumber,
                residentsBySuite.TryGetValue(suiteNumber, out var suiteResidents) ? suiteResidents : [],
                latestBySuite.TryGetValue(suiteNumber, out var sweep) ? ToDto(sweep) : null,
                sweep is null ? [] : LoadPhotosForReport(sweep.Photos)))
            .ToList();
        return new DormSweepReportDto(current.ResidenceHallName, suites);
    }

    public async Task<DormSuiteSweepDto> SubmitAsync(string suiteNumber, SubmitDormSuiteSweepRequest request,
        CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var normalizedSuiteNumber = NormalizeSuiteNumber(suiteNumber);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > 2000) throw new AppException(400, "NOTES_TOO_LONG", "Notes must be 2,000 characters or fewer.");

        // A sweep update creates a new response; retain the latest suite's photos on that response.
        var previous = await db.DormSuiteSweeps.Include(x => x.Photos)
            .Where(x => x.ResidenceHallId == current.ResidenceHallId && x.SuiteNumber == normalizedSuiteNumber)
            .OrderByDescending(x => x.CheckedAt).FirstOrDefaultAsync(cancellationToken);

        var sweep = new DormSuiteSweep
        {
            ResidenceHallId = current.ResidenceHallId,
            SuiteNumber = normalizedSuiteNumber,
            CheckedByUserId = current.Id,
            IsCommonAreaClean = request.IsCommonAreaClean,
            HasMoldOrLeak = request.HasMoldOrLeak,
            HasFurnitureMovedToCommonArea = request.HasFurnitureMovedToCommonArea,
            IsBathroomClean = request.IsBathroomClean,
            AreToiletsAndSinksWorking = request.AreToiletsAndSinksWorking,
            AreShowersWorking = request.AreShowersWorking,
            SmellsLikeMarijuanaOrAlcohol = request.SmellsLikeMarijuanaOrAlcohol,
            Notes = notes
        };

        db.DormSuiteSweeps.Add(sweep);
        if (previous is not null)
            foreach (var photo in previous.Photos.ToList()) photo.DormSuiteSweep = sweep;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "DORM_SUITE_SWEEP_COMPLETED",
            EntityType = "DormSuite",
            EntityId = $"{current.ResidenceHallId}:{normalizedSuiteNumber}",
            NewValuesJson = JsonSerializer.Serialize(new { Suite = normalizedSuiteNumber, sweep.CheckedAt })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new DormSuiteSweepDto(sweep.Id, normalizedSuiteNumber, current.Id, $"{current.FirstName} {current.LastName}",
            sweep.CheckedAt, sweep.IsCommonAreaClean, sweep.HasMoldOrLeak, sweep.HasFurnitureMovedToCommonArea,
            sweep.IsBathroomClean, sweep.AreToiletsAndSinksWorking, sweep.AreShowersWorking,
            sweep.SmellsLikeMarijuanaOrAlcohol, sweep.Notes, HasConcerns(sweep));
    }

    public async Task<DormSweepResetDto> ResetAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var sweeps = await db.DormSuiteSweeps
            .Include(sweep => sweep.Photos)
            .Where(sweep => sweep.ResidenceHallId == current.ResidenceHallId)
            .ToListAsync(cancellationToken);
        if (sweeps.Count == 0) return new DormSweepResetDto(0);

        var storedPhotos = sweeps.SelectMany(sweep => sweep.Photos).Select(photo => photo.StoredFileName).ToList();
        db.DormSuiteSweeps.RemoveRange(sweeps);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "DORM_SWEEPS_RESET",
            EntityType = "ResidenceHall",
            EntityId = current.ResidenceHallId.ToString(),
            OldValuesJson = JsonSerializer.Serialize(new { SweepCount = sweeps.Count, PhotoCount = storedPhotos.Count })
        });
        await db.SaveChangesAsync(cancellationToken);

        foreach (var storedPhoto in storedPhotos) DeleteStoredPhoto(storedPhoto);
        return new DormSweepResetDto(sweeps.Count, storedPhotos.Count);
    }

    private static string NormalizeSuiteNumber(string suiteNumber)
    {
        var cleaned = suiteNumber.Trim().ToUpperInvariant()
            .Replace("ELTS", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (!int.TryParse(cleaned, out var number) || number < 1 || number > SuiteCount)
        {
            throw new AppException(400, "INVALID_SUITE_NUMBER", "Use a suite number from 01 through 25.");
        }

        return number.ToString("00");
    }

    private static DormSuiteSweepSummaryDto ToSummaryDto(DormSuiteSweep sweep) => new(sweep.Id,
        sweep.CheckedByUserId, $"{sweep.CheckedByUser.FirstName} {sweep.CheckedByUser.LastName}",
        sweep.CheckedAt, HasConcerns(sweep));

    private static DormSuiteSweepDto ToDto(DormSuiteSweep sweep) => new(sweep.Id, sweep.SuiteNumber,
        sweep.CheckedByUserId, $"{sweep.CheckedByUser.FirstName} {sweep.CheckedByUser.LastName}",
        sweep.CheckedAt, sweep.IsCommonAreaClean, sweep.HasMoldOrLeak, sweep.HasFurnitureMovedToCommonArea,
        sweep.IsBathroomClean, sweep.AreToiletsAndSinksWorking, sweep.AreShowersWorking,
        sweep.SmellsLikeMarijuanaOrAlcohol, sweep.Notes, HasConcerns(sweep), sweep.Photos.Count,
        sweep.Photos.OrderBy(photo => photo.UploadedAt).Select(photo => new DormSweepPhotoDto(photo.Id,
            photo.OriginalFileName, photo.ContentType, photo.SizeBytes, photo.UploadedAt)).ToList());

    private static bool HasConcerns(DormSuiteSweep sweep) =>
        !sweep.IsCommonAreaClean || sweep.HasMoldOrLeak || sweep.HasFurnitureMovedToCommonArea ||
        !sweep.IsBathroomClean || !sweep.AreToiletsAndSinksWorking || !sweep.AreShowersWorking ||
        sweep.SmellsLikeMarijuanaOrAlcohol;

    private async Task<Dictionary<string, DormSuiteSweep>> LoadLatestSweepsAsync(Guid residenceHallId, CancellationToken cancellationToken)
    {
        var sweeps = await db.DormSuiteSweeps.AsNoTracking()
            .Include(x => x.CheckedByUser)
            .Include(x => x.Photos)
            .Where(x => x.ResidenceHallId == residenceHallId)
            .OrderByDescending(x => x.CheckedAt)
            .ToListAsync(cancellationToken);
        return sweeps.GroupBy(x => x.SuiteNumber).ToDictionary(x => x.Key, x => x.First());
    }

    private static string RoomCode(DormRoom room) => $"ELTS-{room.SuiteNumber}{room.RoomLetter}";

    private static string ResolvePhotoStorageRoot(IConfiguration? configuration)
    {
        return DormSweepPhotoStorage.ResolveRoot(configuration);
    }

    private IReadOnlyList<DormSweepReportPhotoDto> LoadPhotosForReport(IEnumerable<DormSweepPhoto> photos) => photos
        .OrderBy(photo => photo.UploadedAt)
        .Select(photo => new { Photo = photo, Path = SafePhotoPath(photo.StoredFileName) })
        .Where(item => File.Exists(item.Path))
        .Select(item => new DormSweepReportPhotoDto(item.Photo.OriginalFileName, item.Photo.ContentType, File.ReadAllBytes(item.Path)))
        .ToList();

    private void DeleteStoredPhoto(string storedFileName)
    {
        if (!string.Equals(storedFileName, Path.GetFileName(storedFileName), StringComparison.Ordinal)) return;
        try
        {
            var path = SafePhotoPath(storedFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string SafePhotoPath(string storedFileName)
    {
        var path = Path.GetFullPath(Path.Combine(photoStorageRoot, storedFileName));
        if (!path.StartsWith(photoStorageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dorm sweep photo path is invalid.");
        return path;
    }
}
