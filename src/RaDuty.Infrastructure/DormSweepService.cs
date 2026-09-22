using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormSweepService(RaDutyDbContext db, ICurrentUserService currentUserService) : IDormSweepService
{
    private const int SuiteCount = 25;

    public async Task<IReadOnlyList<DormSweepSuiteDto>> GetSuitesAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var latestBySuite = await LoadLatestSweepsAsync(current.ResidenceHallId, cancellationToken);

        return Enumerable.Range(1, SuiteCount)
            .Select(number => number.ToString("00"))
            .Select(suiteNumber => new DormSweepSuiteDto(suiteNumber,
                latestBySuite.TryGetValue(suiteNumber, out var sweep) ? ToSummaryDto(sweep) : null))
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
                latestBySuite.TryGetValue(suiteNumber, out var sweep) ? ToDto(sweep) : null))
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
        sweep.SmellsLikeMarijuanaOrAlcohol, sweep.Notes, HasConcerns(sweep));

    private static bool HasConcerns(DormSuiteSweep sweep) =>
        !sweep.IsCommonAreaClean || sweep.HasMoldOrLeak || sweep.HasFurnitureMovedToCommonArea ||
        !sweep.IsBathroomClean || !sweep.AreToiletsAndSinksWorking || !sweep.AreShowersWorking ||
        sweep.SmellsLikeMarijuanaOrAlcohol;

    private async Task<Dictionary<string, DormSuiteSweep>> LoadLatestSweepsAsync(Guid residenceHallId, CancellationToken cancellationToken)
    {
        var sweeps = await db.DormSuiteSweeps.AsNoTracking()
            .Include(x => x.CheckedByUser)
            .Where(x => x.ResidenceHallId == residenceHallId)
            .OrderByDescending(x => x.CheckedAt)
            .ToListAsync(cancellationToken);
        return sweeps.GroupBy(x => x.SuiteNumber).ToDictionary(x => x.Key, x => x.First());
    }

    private static string RoomCode(DormRoom room) => $"ELTS-{room.SuiteNumber}{room.RoomLetter}";
}
