using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDormCheckPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DormCheckPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DormRoomCheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormCheckPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DormCheckPhotos_DormRoomChecks_DormRoomCheckId",
                        column: x => x.DormRoomCheckId,
                        principalTable: "DormRoomChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DormCheckPhotos_DormRoomCheckId",
                table: "DormCheckPhotos",
                column: "DormRoomCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_DormCheckPhotos_StoredFileName",
                table: "DormCheckPhotos",
                column: "StoredFileName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DormCheckPhotos");
        }
    }
}
