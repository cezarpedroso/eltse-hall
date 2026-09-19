using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDormSuiteSweeps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DormSuiteSweeps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResidenceHallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuiteNumber = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HasTrashInCommonArea = table.Column<bool>(type: "bit", nullable: false),
                    HasTrashInBathroom = table.Column<bool>(type: "bit", nullable: false),
                    HasFurnitureMovedToCommonArea = table.Column<bool>(type: "bit", nullable: false),
                    HasBathroomIssue = table.Column<bool>(type: "bit", nullable: false),
                    HasCommonAreaDamage = table.Column<bool>(type: "bit", nullable: false),
                    NeedsFollowUp = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormSuiteSweeps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DormSuiteSweeps_ResidenceHalls_ResidenceHallId",
                        column: x => x.ResidenceHallId,
                        principalTable: "ResidenceHalls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DormSuiteSweeps_Users_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DormSuiteSweeps_CheckedByUserId",
                table: "DormSuiteSweeps",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DormSuiteSweeps_ResidenceHallId_SuiteNumber_CheckedAt",
                table: "DormSuiteSweeps",
                columns: new[] { "ResidenceHallId", "SuiteNumber", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DormSuiteSweeps");
        }
    }
}
