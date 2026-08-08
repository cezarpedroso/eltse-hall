using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class DormCheckTests
{
    [Fact]
    public async Task Room_check_is_saved_with_the_current_ra()
    {
        await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var ra = new User
        {
            SchoolEmail = "ra@example.edu", FirstName = "Jordan", LastName = "Lee",
            Role = HallRole.ResidentAssistant
        };
        var room = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "A" };
        room.Residents.Add(new DormResident { FirstName = "Alex", LastName = "Rivera" });
        db.AddRange(hall, ra, room);
        await db.SaveChangesAsync();
        var current = new CurrentUserDto(ra.Id, ra.SchoolEmail, ra.FirstName, ra.LastName,
            null, null, ra.Role, true, hall.Id, hall.Name);
        var service = new DormCheckService(db, new StubCurrentUserService(current));

        var saved = await service.SubmitAsync(room.Id, new SubmitDormRoomCheckRequest(
            true, true, true, true, true, null, true, true, "No issues."), CancellationToken.None);

        Assert.Equal("Jordan Lee", saved.CheckedByName);
        Assert.Null(saved.IsCommonAreaClean);
        Assert.Equal("No issues.", saved.Notes);
        Assert.Equal(ra.Id, (await db.DormRoomChecks.SingleAsync()).CheckedByUserId);
    }

    [Fact]
    public async Task Room_check_photo_is_stored_and_removed_when_checks_are_reset()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"raduty-photo-test-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RaDutyDbContext(new DbContextOptionsBuilder<RaDutyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var hall = new ResidenceHall { Name = "Eltse Hall" };
            var ra = new User
            {
                SchoolEmail = "ra@example.edu", FirstName = "Jordan", LastName = "Lee",
                Role = HallRole.ResidentAssistant
            };
            var room = new DormRoom { ResidenceHall = hall, SuiteNumber = "01", RoomLetter = "A" };
            var check = new DormRoomCheck { DormRoom = room, CheckedByUser = ra };
            db.AddRange(hall, ra, room, check);
            await db.SaveChangesAsync();
            var current = new CurrentUserDto(ra.Id, ra.SchoolEmail, ra.FirstName, ra.LastName,
                null, null, ra.Role, true, hall.Id, hall.Name);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DormCheckPhotos:StoragePath"] = storagePath
            }).Build();
            var currentUserService = new StubCurrentUserService(current);
            var service = new DormCheckPhotoService(db, currentUserService, configuration);
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            var added = await service.AddAsync(check.Id,
                [new DormCheckPhotoUpload("room.png", "image/png", png.Length, new MemoryStream(png))],
                CancellationToken.None);
            var returned = await service.GetAsync(added.Single().Id, CancellationToken.None);

            Assert.Equal("room.png", returned.FileName);
            Assert.Equal("image/png", returned.ContentType);
            Assert.Equal(png, returned.Content);
            Assert.Single(await db.DormCheckPhotos.ToListAsync());
            Assert.Single(Directory.GetFiles(storagePath));

            var reset = await new DormCheckService(db, currentUserService, configuration).ResetAsync(CancellationToken.None);

            Assert.Equal(1, reset.DeletedChecks);
            Assert.Equal(1, reset.DeletedPhotos);
            Assert.Empty(await db.DormRoomChecks.ToListAsync());
            Assert.Empty(await db.DormCheckPhotos.ToListAsync());
            Assert.Empty(Directory.GetFiles(storagePath));
            Assert.Contains(await db.AuditLogs.ToListAsync(), audit => audit.Action == "DORM_CHECKS_RESET" && audit.ActorUserId == ra.Id);
        }
        finally
        {
            if (Directory.Exists(storagePath)) Directory.Delete(storagePath, true);
        }
    }

    private sealed class StubCurrentUserService(CurrentUserDto current) : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
    }
}
