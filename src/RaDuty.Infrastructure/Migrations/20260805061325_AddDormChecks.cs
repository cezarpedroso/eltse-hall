using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDormChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DormRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResidenceHallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuiteNumber = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RoomLetter = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DormRooms_ResidenceHalls_ResidenceHallId",
                        column: x => x.ResidenceHallId,
                        principalTable: "ResidenceHalls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DormResidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DormRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SportOrActivity = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SourceRow = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormResidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DormResidents_DormRooms_DormRoomId",
                        column: x => x.DormRoomId,
                        principalTable: "DormRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DormRoomChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DormRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsRoomClean = table.Column<bool>(type: "bit", nullable: false),
                    IsAllFurniturePresent = table.Column<bool>(type: "bit", nullable: false),
                    IsSmokeDetectorClear = table.Column<bool>(type: "bit", nullable: false),
                    IsRoomOdorFree = table.Column<bool>(type: "bit", nullable: false),
                    IsRoomTrashFree = table.Column<bool>(type: "bit", nullable: false),
                    IsCommonAreaClean = table.Column<bool>(type: "bit", nullable: true),
                    IsRoomAlcoholFree = table.Column<bool>(type: "bit", nullable: false),
                    IsRoomDamageFree = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormRoomChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DormRoomChecks_DormRooms_DormRoomId",
                        column: x => x.DormRoomId,
                        principalTable: "DormRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DormRoomChecks_Users_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DormResidents_DormRoomId",
                table: "DormResidents",
                column: "DormRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_DormRoomChecks_CheckedByUserId",
                table: "DormRoomChecks",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DormRoomChecks_DormRoomId_CheckedAt",
                table: "DormRoomChecks",
                columns: new[] { "DormRoomId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DormRooms_ResidenceHallId_SuiteNumber_RoomLetter",
                table: "DormRooms",
                columns: new[] { "ResidenceHallId", "SuiteNumber", "RoomLetter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DormResidents");

            migrationBuilder.DropTable(
                name: "DormRoomChecks");

            migrationBuilder.DropTable(
                name: "DormRooms");
        }
    }
}
