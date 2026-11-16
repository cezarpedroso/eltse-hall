using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class ResidentRosterExportTests
{
    [Fact]
    public async Task Export_contains_only_the_selected_hall_with_resident_details_and_room_counts()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var selectedHall = new ResidenceHall { Name = "Eltse Hall", Code = "ELTS" };
        var otherHall = new ResidenceHall { Name = "Lewis Hall", Code = "LEWS" };
        var occupied = new DormRoom { ResidenceHall = selectedHall, SuiteNumber = "01", RoomLetter = "A", Capacity = 2 };
        occupied.Residents.Add(new DormResident { FirstName = "Alex", LastName = "Rivera", SportOrActivity = "Soccer" });
        occupied.Residents.Add(new DormResident { FirstName = "Jamie", LastName = "Chen", SportOrActivity = "=HYPERLINK(\"https://example.com\",\"Click\")" });
        var empty = new DormRoom { ResidenceHall = selectedHall, SuiteNumber = "01", RoomLetter = "B", Capacity = 2 };
        var otherRoom = new DormRoom { ResidenceHall = otherHall, SuiteNumber = "A201", RoomLetter = "", Capacity = 1 };
        otherRoom.Residents.Add(new DormResident { FirstName = "Outside", LastName = "Student" });
        db.AddRange(selectedHall, otherHall, occupied, empty, otherRoom);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(Guid.NewGuid(), "ra@wmpenn.edu", "Jordan", "Lee",
            null, null, HallRole.ResidentAssistant, true, selectedHall.Id, selectedHall.Name);

        var export = await new ResidentRosterExportService(db, new StubCurrentUserService(current))
            .ExportAsync(CancellationToken.None);

        Assert.StartsWith("eltse-hall-residents-", export.FileName);
        Assert.EndsWith(".xlsx", export.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(export.Content));
        var sheet = workbook.Worksheet("Residents");
        Assert.Equal("RESIDENT ROSTER", sheet.Cell("A2").GetString());
        Assert.Equal("Eltse Hall", sheet.Cell("A4").GetString());
        Assert.Equal(2, sheet.Cell("A8").GetValue<int>());
        Assert.Equal(1, sheet.Cell("C8").GetValue<int>());
        Assert.Equal(2, sheet.Cell("E8").GetValue<int>());
        Assert.Equal("Room", sheet.Cell("A12").GetString());
        Assert.Equal("ELTS-01A", sheet.Cell("A13").GetString());
        Assert.Equal("Jamie", sheet.Cell("B13").GetString());
        Assert.False(sheet.Cell("D13").HasFormula);
        Assert.StartsWith("=HYPERLINK", sheet.Cell("D13").GetString());
        Assert.Equal("Alex", sheet.Cell("B14").GetString());
        Assert.Equal("Soccer", sheet.Cell("D14").GetString());
        Assert.Equal(2, sheet.Cell("E14").GetValue<int>());
        Assert.Equal(2, sheet.Cell("F14").GetValue<int>());
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.GetString().Contains("Outside", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Empty_hall_still_exports_a_readable_roster()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Watson Hall", Code = "WATS" };
        db.Add(hall);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(Guid.NewGuid(), "ra@wmpenn.edu", "Jordan", "Lee",
            null, null, HallRole.ResidentAssistant, true, hall.Id, hall.Name);

        var export = await new ResidentRosterExportService(db, new StubCurrentUserService(current))
            .ExportAsync(CancellationToken.None);

        using var workbook = new XLWorkbook(new MemoryStream(export.Content));
        var sheet = workbook.Worksheet("Residents");
        Assert.Equal(0, sheet.Cell("A8").GetValue<int>());
        Assert.Contains("No residents", sheet.Cell("A13").GetString());
    }

    private sealed class StubCurrentUserService(CurrentUserDto current) : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
    }
}
