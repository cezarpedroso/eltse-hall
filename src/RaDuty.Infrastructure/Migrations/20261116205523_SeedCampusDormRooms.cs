using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations;

public partial class SeedCampusDormRooms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "SuiteNumber", table: "DormSuiteSweeps", type: "nvarchar(4)", maxLength: 4,
            nullable: false, oldClrType: typeof(string), oldType: "nvarchar(2)", oldMaxLength: 2);
        migrationBuilder.AlterColumn<string>(
            name: "SuiteNumber", table: "DormRooms", type: "nvarchar(4)", maxLength: 4,
            nullable: false, oldClrType: typeof(string), oldType: "nvarchar(2)", oldMaxLength: 2);
        migrationBuilder.AddColumn<int>(
            name: "Capacity", table: "DormRooms", type: "int", nullable: false, defaultValue: 2);

        migrationBuilder.Sql("""
            UPDATE [ResidenceHalls] SET [Code] = N'ELTS'
            WHERE [Id] = '10000000-0000-0000-0000-000000000001' AND [Code] = N''
                AND NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'ELTS');
            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'ELTS')
            BEGIN
                IF EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Id] = '10000000-0000-0000-0000-000000000001')
                    INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                    VALUES (NEWID(), N'Eltse Hall', N'ELTS', N'America/Chicago', CAST(1 AS bit));
                ELSE
                    INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                    VALUES ('10000000-0000-0000-0000-000000000001', N'Eltse Hall', N'ELTS', N'America/Chicago', CAST(1 AS bit));
            END;
            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'ROS1')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Rosenberger Apartments 1', N'ROS1', N'America/Chicago', CAST(1 AS bit));
            IF NOT EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'ROS2')
                INSERT INTO [ResidenceHalls] ([Id], [Name], [Code], [TimeZone], [IsActive])
                VALUES (NEWID(), N'Rosenberger Apartments 2', N'ROS2', N'America/Chicago', CAST(1 AS bit));
            """);

        using var stream = typeof(SeedCampusDormRooms).Assembly.GetManifestResourceStream(
            "RaDuty.Infrastructure.SeedData.campus-dorm-rooms.json")
            ?? throw new InvalidOperationException("The campus dorm room inventory is missing.");
        using var inventory = JsonDocument.Parse(stream);
        var groups = inventory.RootElement.EnumerateArray().GroupBy(room => room.GetProperty("hall").GetString());
        foreach (var group in groups)
        {
            var hallCode = group.Key switch { "LEWI" => "LEWS", "TWIN" => "TWRS", _ => group.Key };
            if (hallCode is null) throw new InvalidOperationException("A room is missing its hall code.");
            var values = group.Select(room => $"(N'{Escape(room.GetProperty("suite").GetString()!)}', N'{Escape(room.GetProperty("letter").GetString()!)}', {room.GetProperty("capacity").GetInt32()})");
            migrationBuilder.Sql($"""
                DECLARE @HallId uniqueidentifier = (SELECT TOP (1) [Id] FROM [ResidenceHalls] WHERE [Code] = N'{Escape(hallCode)}');
                IF @HallId IS NULL THROW 50000, 'A campus residence hall is missing during room import.', 1;
                MERGE [DormRooms] AS target
                USING (VALUES {string.Join(",", values)}) AS source ([SuiteNumber], [RoomLetter], [Capacity])
                ON target.[ResidenceHallId] = @HallId AND target.[SuiteNumber] = source.[SuiteNumber] AND target.[RoomLetter] = source.[RoomLetter]
                WHEN MATCHED THEN UPDATE SET [Capacity] = source.[Capacity]
                WHEN NOT MATCHED THEN INSERT ([Id], [ResidenceHallId], [SuiteNumber], [RoomLetter], [Capacity])
                    VALUES (NEWID(), @HallId, source.[SuiteNumber], source.[RoomLetter], source.[Capacity]);
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Campus room inventory cannot be rolled back without risking assigned residents and checks.");

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
