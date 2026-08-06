using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class DormResidentManagementTests
{
    [Fact]
    public async Task Ra_can_add_move_edit_and_transfer_a_resident_out()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var ra = new User
        {
            EntraObjectId = "dev-ra", SchoolEmail = "ra@example.edu", FirstName = "Jordan", LastName = "Lee",
            Role = HallRole.ResidentAssistant
        };
        var roomA = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "A" };
        var roomB = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "B" };
        db.AddRange(hall, ra, roomA, roomB);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(ra.Id, ra.EntraObjectId, ra.SchoolEmail, ra.FirstName, ra.LastName,
            null, null, ra.Role, true, hall.Id, hall.Name);
        var service = new DormResidentManagementService(db, new StubCurrentUserService(current));

        var added = await service.CreateAsync(new SaveDormResidentRequest("Alex", "Rivera", roomA.Id, "Soccer"), CancellationToken.None);
        var moved = await service.UpdateAsync(added.Id, new SaveDormResidentRequest("Alexis", "Rivera", roomB.Id, "Basketball"), CancellationToken.None);

        Assert.Equal("ELTS-01B", moved.RoomCode);
        Assert.Equal("Alexis", moved.FirstName);
        Assert.Single(await service.GetAsync("Alexis", CancellationToken.None));
        Assert.Equal(1, (await service.GetRoomsAsync(CancellationToken.None)).Single(room => room.Id == roomB.Id).Occupancy);

        await service.RemoveAsync(added.Id, CancellationToken.None);

        Assert.Empty(await db.DormResidents.ToListAsync());
        var actions = await db.AuditLogs.Select(audit => audit.Action).ToListAsync();
        Assert.Contains("DORM_RESIDENT_ADDED", actions);
        Assert.Contains("DORM_RESIDENT_UPDATED", actions);
        Assert.Contains("DORM_RESIDENT_TRANSFERRED_OUT", actions);
    }

    [Fact]
    public async Task A_resident_cannot_be_moved_into_a_full_room()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var ra = new User
        {
            EntraObjectId = "dev-ra", SchoolEmail = "ra@example.edu", FirstName = "Jordan", LastName = "Lee",
            Role = HallRole.ResidentAssistant
        };
        var roomA = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "A" };
        var roomB = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "B" };
        var moving = new DormResident { DormRoom = roomA, FirstName = "Moving", LastName = "Resident" };
        roomB.Residents.Add(new DormResident { FirstName = "First", LastName = "Roommate" });
        roomB.Residents.Add(new DormResident { FirstName = "Second", LastName = "Roommate" });
        db.AddRange(hall, ra, roomA, roomB, moving);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(ra.Id, ra.EntraObjectId, ra.SchoolEmail, ra.FirstName, ra.LastName,
            null, null, ra.Role, true, hall.Id, hall.Name);
        var service = new DormResidentManagementService(db, new StubCurrentUserService(current));

        var error = await Assert.ThrowsAsync<AppException>(() => service.UpdateAsync(moving.Id,
            new SaveDormResidentRequest("Moving", "Resident", roomB.Id, null), CancellationToken.None));

        Assert.Equal("DORM_ROOM_AT_CAPACITY", error.Code);
    }

    private sealed class StubCurrentUserService(CurrentUserDto current) : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
    }
}
