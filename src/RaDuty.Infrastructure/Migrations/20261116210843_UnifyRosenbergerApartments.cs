using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyRosenbergerApartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceCode",
                table: "DormRooms",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE room SET [SourceCode] =
                    CASE hall.[Code]
                        WHEN N'LEWS' THEN N'LEWI'
                        WHEN N'TWRS' THEN N'TWIN'
                        ELSE hall.[Code]
                    END + N'-' + room.[SuiteNumber] + room.[RoomLetter]
                FROM [DormRooms] AS room
                INNER JOIN [ResidenceHalls] AS hall ON hall.[Id] = room.[ResidenceHallId]
                WHERE room.[SourceCode] IS NULL;

                DECLARE @Rosenberger1 uniqueidentifier = (SELECT TOP (1) [Id] FROM [ResidenceHalls] WHERE [Code] = N'ROS1');
                DECLARE @Rosenberger2 uniqueidentifier = (SELECT TOP (1) [Id] FROM [ResidenceHalls] WHERE [Code] = N'ROS2');
                IF @Rosenberger1 IS NULL OR @Rosenberger2 IS NULL
                    THROW 50000, 'Both Rosenberger hall records are required for the merge.', 1;
                IF EXISTS (SELECT 1 FROM [ResidenceHalls] WHERE [Code] = N'ROS')
                    THROW 50000, 'A unified Rosenberger hall already exists.', 1;
                IF EXISTS (
                    SELECT 1 FROM [DormRooms] AS secondary
                    INNER JOIN [DormRooms] AS primaryRoom
                        ON primaryRoom.[ResidenceHallId] = @Rosenberger1
                        AND primaryRoom.[SuiteNumber] = secondary.[SuiteNumber]
                        AND primaryRoom.[RoomLetter] = secondary.[RoomLetter]
                    WHERE secondary.[ResidenceHallId] = @Rosenberger2)
                    THROW 50000, 'Rosenberger rooms have duplicate numbers across buildings.', 1;
                IF EXISTS (
                    SELECT 1 FROM [HallMemberships] AS secondary
                    INNER JOIN [HallMemberships] AS primaryMembership
                        ON primaryMembership.[ResidenceHallId] = @Rosenberger1
                        AND primaryMembership.[UserId] = secondary.[UserId]
                    WHERE secondary.[ResidenceHallId] = @Rosenberger2)
                    THROW 50000, 'Rosenberger has duplicate staff memberships to resolve before merging.', 1;
                IF EXISTS (
                    SELECT 1 FROM [SchedulePeriods] AS secondary
                    INNER JOIN [SchedulePeriods] AS primaryPeriod
                        ON primaryPeriod.[ResidenceHallId] = @Rosenberger1
                        AND primaryPeriod.[Year] = secondary.[Year]
                        AND primaryPeriod.[Month] = secondary.[Month]
                    WHERE secondary.[ResidenceHallId] = @Rosenberger2)
                    THROW 50000, 'Rosenberger has overlapping schedules to resolve before merging.', 1;

                UPDATE [DormRooms] SET [ResidenceHallId] = @Rosenberger1 WHERE [ResidenceHallId] = @Rosenberger2;
                UPDATE [HallMemberships] SET [ResidenceHallId] = @Rosenberger1 WHERE [ResidenceHallId] = @Rosenberger2;
                UPDATE [SchedulePeriods] SET [ResidenceHallId] = @Rosenberger1 WHERE [ResidenceHallId] = @Rosenberger2;
                UPDATE [DormSuiteSweeps] SET [ResidenceHallId] = @Rosenberger1 WHERE [ResidenceHallId] = @Rosenberger2;
                UPDATE [IncidentReports] SET [ResidenceHallId] = @Rosenberger1 WHERE [ResidenceHallId] = @Rosenberger2;
                DELETE FROM [ResidenceHalls] WHERE [Id] = @Rosenberger2;
                UPDATE [ResidenceHalls] SET [Name] = N'Rosenberger Apartments', [Code] = N'ROS'
                    WHERE [Id] = @Rosenberger1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("The Rosenberger hall merge cannot be rolled back without risking assigned residents and checks.");
        }
    }
}
