using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class DormRosterImportTests
{
    [Fact]
    public async Task Director_can_preview_and_apply_room_moves_from_the_eltse_workbook()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var director = new User
        {
            EntraObjectId = "dev-director", SchoolEmail = "director@example.edu", FirstName = "Marisol", LastName = "Reyes",
            Role = HallRole.HallDirector
        };
        var roomA = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "A" };
        var roomB = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "B" };
        var alex = new DormResident { DormRoom = roomA, FirstName = "Alex", LastName = "Lee", SportOrActivity = "Soccer" };
        var former = new DormResident { DormRoom = roomB, FirstName = "Former", LastName = "Resident" };
        db.AddRange(hall, director, roomA, roomB, alex, former);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(director.Id, director.EntraObjectId, director.SchoolEmail, director.FirstName,
            director.LastName, null, null, director.Role, true, hall.Id, hall.Name);
        var service = new DormRosterImportService(db, new StubCurrentUserService(current));
        var workbookBytes = WorkbookBytes();

        await using var previewStream = new MemoryStream(workbookBytes);
        var preview = await service.PreviewAsync(new DormRosterWorkbookUpload("residents.xlsx", previewStream.Length, previewStream), CancellationToken.None);

        Assert.True(preview.CanApply);
        Assert.Equal(2, preview.ResidentCount);
        Assert.Equal(2, preview.OccupiedRooms);
        Assert.Equal(1, preview.AddedResidents);
        Assert.Equal(1, preview.RemovedResidents);
        Assert.Equal(1, preview.MovedResidents);
        Assert.Equal(1, preview.IgnoredRows);
        Assert.Contains(preview.Changes, change => change.Type == "Moved" && change.FirstName == "Alex" && change.FromRoom == "ELTS-01A" && change.ToRoom == "ELTS-01B");

        await using var applyStream = new MemoryStream(workbookBytes);
        var applied = await service.ApplyAsync(new DormRosterWorkbookUpload("residents.xlsx", applyStream.Length, applyStream), CancellationToken.None);
        var residents = await db.DormResidents.OrderBy(resident => resident.LastName).ToListAsync();

        Assert.True(applied.CanApply);
        Assert.Equal(2, residents.Count);
        Assert.Equal(roomB.Id, residents.Single(resident => resident.Id == alex.Id).DormRoomId);
        Assert.Contains(residents, resident => resident.FirstName == "Priya" && resident.LastName == "Patel" && resident.DormRoomId == roomA.Id);
        Assert.DoesNotContain(residents, resident => resident.Id == former.Id);
        Assert.Contains(await db.AuditLogs.ToListAsync(), audit => audit.Action == "DORM_ROSTER_IMPORTED" && audit.ActorUserId == director.Id);
    }

    private static byte[] WorkbookBytes()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Residents");
        sheet.Cell("A1").Value = "LastName";
        sheet.Cell("B1").Value = "FirstName";
        sheet.Cell("C1").Value = "Room";
        sheet.Cell("D1").Value = "Sport/Activity";
        sheet.Cell("A2").Value = "Lee";
        sheet.Cell("B2").Value = "Alex";
        sheet.Cell("C2").Value = "ELTS-01B";
        sheet.Cell("D2").Value = "Soccer";
        sheet.Cell("A3").Value = "Patel";
        sheet.Cell("B3").Value = "Priya";
        sheet.Cell("C3").Value = "ELTS-01A";
        sheet.Cell("D3").Value = "Basketball";
        sheet.Cell("A4").Value = "Other";
        sheet.Cell("B4").Value = "Hall";
        sheet.Cell("C4").Value = "MARK-101A";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed class StubCurrentUserService(CurrentUserDto current) : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
    }
}
