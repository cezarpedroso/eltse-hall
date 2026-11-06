using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations;

[DbContext(typeof(RaDutyDbContext))]
[Migration("20261106100000_SeedCampusResidenceHalls")]
public partial class SeedCampusResidenceHalls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'MARK')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Market Hall', N'MARK', N'America/Chicago', CAST(1 AS bit));

            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'WATS')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Watson Hall', N'WATS', N'America/Chicago', CAST(1 AS bit));

            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'LEWS')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Lewis Hall', N'LEWS', N'America/Chicago', CAST(1 AS bit));

            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'TWRS')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Twin Towers', N'TWRS', N'America/Chicago', CAST(1 AS bit));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The hall records can receive staff and resident assignments immediately after migration.
        // Deliberately retain them on rollback rather than risking deletion of production records.
    }
}
