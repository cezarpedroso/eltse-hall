using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class IncidentReportService(RaDutyDbContext db, ICurrentUserService currentUserService) : IIncidentReportService
{
    public async Task<IReadOnlyList<IncidentReportSummaryDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        return await db.IncidentReports.AsNoTracking()
            .Where(x => x.ResidenceHallId == current.ResidenceHallId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new IncidentReportSummaryDto(x.Id, x.IncidentType, x.OccurredAt, x.Location,
                x.ReportedByUser.FirstName + " " + x.ReportedByUser.LastName, x.Students.Count, x.InfractionPrice))
            .ToListAsync(cancellationToken);
    }

    public async Task<IncidentReportDto> GetAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var incident = await LoadAsync(incidentId, current.ResidenceHallId, cancellationToken);
        return ToDto(incident);
    }

    public async Task<IncidentReportDto> CreateAsync(CreateIncidentReportRequest request, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var location = RequiredText(request.Location, "LOCATION_REQUIRED", "Enter where the incident happened.", 160);
        var description = RequiredText(request.Description, "DESCRIPTION_REQUIRED", "Enter a description of what happened.", 4000);
        if (request.OccurredAt == default) throw new AppException(400, "OCCURRED_AT_REQUIRED", "Enter when the incident happened.");
        if (request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new AppException(400, "OCCURRED_AT_FUTURE", "The incident time cannot be in the future.");
        if (request.Students is null || request.Students.Count == 0)
            throw new AppException(400, "STUDENT_REQUIRED", "Add at least one involved student.");
        if (request.Students.Count > 20)
            throw new AppException(400, "TOO_MANY_STUDENTS", "A report can include up to 20 students.");
        if (request.InfractionPrice is < 0 or > 1_000_000)
            throw new AppException(400, "INVALID_INFRACTION_PRICE", "Enter an infraction price between $0 and $1,000,000.");

        decimal? alcoholOunces = request.AlcoholOunces;
        if (request.IncidentType == IncidentType.Alcohol)
        {
            if (alcoholOunces is null or <= 0 or > 10_000)
                throw new AppException(400, "ALCOHOL_OUNCES_REQUIRED", "Enter the number of alcohol ounces found.");
        }
        else alcoholOunces = null;

        var students = request.Students.Select(student => new IncidentStudent
        {
            FirstName = RequiredText(student.FirstName, "STUDENT_FIRST_NAME_REQUIRED", "Enter each student's first name.", 80),
            LastName = RequiredText(student.LastName, "STUDENT_LAST_NAME_REQUIRED", "Enter each student's last name.", 80),
            StudentId = RequiredText(student.StudentId, "STUDENT_ID_REQUIRED", "Enter each student's ID.", 40),
            RoomNumber = RequiredText(student.RoomNumber, "ROOM_NUMBER_REQUIRED", "Enter each student's room number.", 10),
            RoomLetter = RequiredText(student.RoomLetter, "ROOM_LETTER_REQUIRED", "Enter each student's room letter.", 4).ToUpperInvariant()
        }).ToList();

        var staffIds = (request.StaffPresentUserIds ?? [])
            .Where(id => id != Guid.Empty && id != current.Id).Distinct().ToList();
        if (staffIds.Count > 10)
            throw new AppException(400, "TOO_MANY_STAFF", "A report can include up to 10 additional staff members.");
        if (staffIds.Count > 0)
        {
            var matched = await db.StaffUsers.AsNoTracking().Where(user => staffIds.Contains(user.Id) && user.IsActive &&
                    user.HallMemberships.Any(membership => membership.ResidenceHallId == current.ResidenceHallId && membership.IsActive))
                .Select(user => user.Id).ToListAsync(cancellationToken);
            if (matched.Count != staffIds.Count)
                throw new AppException(400, "INVALID_STAFF_MEMBER", "Each staff member present must be an active Eltse Hall staff member.");
        }

        var incident = new IncidentReport
        {
            ResidenceHallId = current.ResidenceHallId,
            ReportedByUserId = current.Id,
            IncidentType = request.IncidentType,
            OccurredAt = request.OccurredAt,
            Location = location,
            Description = description,
            AlcoholOunces = alcoholOunces,
            InfractionPrice = request.InfractionPrice,
            Students = students,
            StaffPresent = staffIds.Select(userId => new IncidentStaffMember { UserId = userId }).ToList()
        };
        db.IncidentReports.Add(incident);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "INCIDENT_REPORTED",
            EntityType = "IncidentReport",
            EntityId = incident.Id.ToString(),
            NewValuesJson = JsonSerializer.Serialize(new { incident.IncidentType, incident.OccurredAt, StudentCount = students.Count, StaffCount = staffIds.Count })
        });
        await db.SaveChangesAsync(cancellationToken);
        var saved = await LoadAsync(incident.Id, current.ResidenceHallId, cancellationToken);
        return ToDto(saved);
    }

    private async Task<IncidentReport> LoadAsync(Guid incidentId, Guid residenceHallId, CancellationToken cancellationToken) =>
        await db.IncidentReports.AsNoTracking()
            .Include(x => x.ReportedByUser)
            .Include(x => x.StaffPresent).ThenInclude(x => x.User)
            .Include(x => x.Students)
            .SingleOrDefaultAsync(x => x.Id == incidentId && x.ResidenceHallId == residenceHallId, cancellationToken)
        ?? throw new AppException(404, "INCIDENT_NOT_FOUND", "The incident report was not found.");

    private static IncidentReportDto ToDto(IncidentReport incident) => new(incident.Id, incident.IncidentType,
        incident.OccurredAt, incident.Location, incident.Description, incident.AlcoholOunces, incident.InfractionPrice,
        incident.CreatedAt, incident.ReportedByUserId, Name(incident.ReportedByUser),
        incident.StaffPresent.OrderBy(x => x.User.LastName).ThenBy(x => x.User.FirstName)
            .Select(x => new IncidentStaffDto(x.UserId, Name(x.User))).ToList(),
        incident.Students.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new IncidentStudentDto(x.Id, x.FirstName, x.LastName, x.StudentId, x.RoomNumber, x.RoomLetter)).ToList());

    private static string RequiredText(string? value, string code, string message, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new AppException(400, code, message);
        if (trimmed.Length > maxLength) throw new AppException(400, $"{code}_TOO_LONG", $"{message} It must be {maxLength:N0} characters or fewer.");
        return trimmed;
    }

    private static string Name(User user) => $"{user.FirstName} {user.LastName}";
}
