using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class RaDutyDbContext(DbContextOptions<RaDutyDbContext> options)
    : IdentityUserContext<ApplicationAccount, Guid>(options)
{
    public DbSet<User> StaffUsers => Set<User>();
    public DbSet<ResidenceHall> ResidenceHalls => Set<ResidenceHall>();
    public DbSet<HallMembership> HallMemberships => Set<HallMembership>();
    public DbSet<SchedulePeriod> SchedulePeriods => Set<SchedulePeriod>();
    public DbSet<DutyShift> DutyShifts => Set<DutyShift>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DormRoom> DormRooms => Set<DormRoom>();
    public DbSet<DormResident> DormResidents => Set<DormResident>();
    public DbSet<DormRoomCheck> DormRoomChecks => Set<DormRoomCheck>();
    public DbSet<DormCheckPhoto> DormCheckPhotos => Set<DormCheckPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationAccount>(entity =>
        {
            entity.Property(x => x.MustChangePassword).HasDefaultValue(false);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne(x => x.User).WithOne().HasForeignKey<ApplicationAccount>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(x => x.SchoolEmail).IsUnique();
            entity.Property(x => x.SchoolEmail).HasMaxLength(254);
            entity.Property(x => x.FirstName).HasMaxLength(80);
            entity.Property(x => x.LastName).HasMaxLength(80);
            entity.Property(x => x.RoomNumber).HasMaxLength(30);
            entity.Property(x => x.PhoneNumber).HasMaxLength(30);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ResidenceHall>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.TimeZone).HasMaxLength(80);
        });

        modelBuilder.Entity<HallMembership>(entity =>
        {
            entity.HasIndex(x => new { x.ResidenceHallId, x.UserId }).IsUnique();
            entity.Property(x => x.HallRole).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(x => x.ResidenceHall).WithMany(x => x.Memberships).HasForeignKey(x => x.ResidenceHallId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany(x => x.HallMemberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulePeriod>(entity =>
        {
            entity.HasIndex(x => new { x.ResidenceHallId, x.Year, x.Month }).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(x => x.ResidenceHall).WithMany(x => x.SchedulePeriods).HasForeignKey(x => x.ResidenceHallId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DutyShift>(entity =>
        {
            entity.HasIndex(x => new { x.SchedulePeriodId, x.DutyDate }).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne(x => x.SchedulePeriod).WithMany(x => x.Shifts).HasForeignKey(x => x.SchedulePeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.HasIndex(x => new { x.DutyShiftId, x.UserId }).IsUnique().HasFilter("[RemovedAt] IS NULL");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasOne(x => x.DutyShift).WithMany(x => x.Assignments).HasForeignKey(x => x.DutyShiftId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.Property(x => x.EntityId).HasMaxLength(100);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<DormRoom>(entity =>
        {
            entity.HasIndex(x => new { x.ResidenceHallId, x.SuiteNumber, x.RoomLetter }).IsUnique();
            entity.Property(x => x.SuiteNumber).HasMaxLength(2);
            entity.Property(x => x.RoomLetter).HasMaxLength(1);
            entity.HasOne(x => x.ResidenceHall).WithMany(x => x.DormRooms).HasForeignKey(x => x.ResidenceHallId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DormResident>(entity =>
        {
            entity.Property(x => x.FirstName).HasMaxLength(80);
            entity.Property(x => x.LastName).HasMaxLength(80);
            entity.Property(x => x.SportOrActivity).HasMaxLength(120);
            entity.HasOne(x => x.DormRoom).WithMany(x => x.Residents).HasForeignKey(x => x.DormRoomId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DormRoomCheck>(entity =>
        {
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.DormRoomId, x.CheckedAt });
            entity.HasOne(x => x.DormRoom).WithMany(x => x.Checks).HasForeignKey(x => x.DormRoomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CheckedByUser).WithMany().HasForeignKey(x => x.CheckedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DormCheckPhoto>(entity =>
        {
            entity.Property(x => x.OriginalFileName).HasMaxLength(180);
            entity.Property(x => x.StoredFileName).HasMaxLength(80);
            entity.Property(x => x.ContentType).HasMaxLength(80);
            entity.HasIndex(x => x.StoredFileName).IsUnique();
            entity.HasOne(x => x.DormRoomCheck).WithMany(x => x.Photos).HasForeignKey(x => x.DormRoomCheckId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
