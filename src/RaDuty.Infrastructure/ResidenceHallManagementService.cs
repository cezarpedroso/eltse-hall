using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class ResidenceHallManagementService(RaDutyDbContext db, ICurrentUserService currentUserService) : IResidenceHallManagementService
{
    public async Task<IReadOnlyList<ResidenceHallDto>> GetAsync(CancellationToken cancellationToken) =>
        await db.ResidenceHalls.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new ResidenceHallDto(x.Id, x.Name, x.Code, x.TimeZone, x.IsActive,
                x.Memberships.Count(membership => membership.IsActive && membership.User.IsActive), x.DormRooms.Count))
            .ToListAsync(cancellationToken);

    public async Task<ResidenceHallDto> CreateAsync(CreateResidenceHallRequest request, CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetAsync(cancellationToken);
        if (!IsCampusAdministrator(actor.Role)) throw new AppException(403, "CAMPUS_ADMIN_REQUIRED", "Campus administrator access is required.");
        var name = Clean(request.Name, 160, "Enter a hall name.");
        var code = Clean(request.Code, 12, "Enter a hall code.").ToUpperInvariant();
        if (!code.All(char.IsLetterOrDigit)) throw new AppException(400, "INVALID_HALL_CODE", "Hall code can contain letters and numbers only.");
        if (await db.ResidenceHalls.AnyAsync(x => x.Code == code, cancellationToken))
            throw new AppException(409, "HALL_CODE_EXISTS", "A hall already uses that code.");
        var hall = new ResidenceHall { Name = name, Code = code, TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "America/Chicago" : request.TimeZone.Trim() };
        db.ResidenceHalls.Add(hall);
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor.Id, Action = "RESIDENCE_HALL_CREATED", EntityType = "ResidenceHall", EntityId = hall.Id.ToString(), NewValuesJson = JsonSerializer.Serialize(new { hall.Name, hall.Code }) });
        await db.SaveChangesAsync(cancellationToken);
        return new ResidenceHallDto(hall.Id, hall.Name, hall.Code, hall.TimeZone, hall.IsActive, 0, 0);
    }

    private static bool IsCampusAdministrator(HallRole role) => role is HallRole.Admin or HallRole.DeanOfStudents or HallRole.AssistantDirectorOfResidenceLife;
    private static string Clean(string? value, int maxLength, string message)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new AppException(400, "HALL_VALUE_REQUIRED", message);
        if (result.Length > maxLength) throw new AppException(400, "HALL_VALUE_TOO_LONG", $"{message} It must be {maxLength} characters or fewer.");
        return result;
    }
}
