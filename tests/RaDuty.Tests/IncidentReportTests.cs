using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class IncidentReportTests
{
    [Fact]
    public async Task Authorized_staff_can_save_an_incident_report_for_the_active_hall()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var reporter = new User
        {
            SchoolEmail = "ra@wmpenn.edu", FirstName = "Jordan", LastName = "Lee",
            Role = HallRole.ResidentAssistant
        };
        reporter.HallMemberships.Add(new HallMembership
        {
            ResidenceHall = hall, User = reporter, HallRole = HallRole.ResidentAssistant
        });
        db.AddRange(hall, reporter);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(reporter.Id, reporter.SchoolEmail, reporter.FirstName, reporter.LastName,
            null, null, HallRole.ResidentAssistant, true, hall.Id, hall.Name);
        var service = new IncidentReportService(db, new StubCurrentUserService(current));

        var saved = await service.CreateAsync(new CreateIncidentReportRequest(
            IncidentType.Alcohol, DateTimeOffset.UtcNow.AddMinutes(-10), "ELTS-12B", "Alcohol was found in the room.",
            12, 25, [], [new SaveIncidentStudentRequest("Alex", "Rivera", "123456", "12", "B")]),
            CancellationToken.None);

        Assert.Equal(IncidentType.Alcohol, saved.IncidentType);
        Assert.Equal("ELTS-12B", saved.Location);
        Assert.Equal(12, saved.AlcoholOunces);
        Assert.Equal(25, saved.InfractionPrice);
        var student = Assert.Single(saved.Students);
        Assert.Equal(("Alex", "Rivera", "123456", "12", "B"),
            (student.FirstName, student.LastName, student.StudentId, student.RoomNumber, student.RoomLetter));
        Assert.Single(await service.GetAllAsync(CancellationToken.None));
    }

    private sealed class StubCurrentUserService(CurrentUserDto current) : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
    }
}
