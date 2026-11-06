using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampusHallRegistryAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ResidenceHalls",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ResidenceHalls_Code",
                table: "ResidenceHalls",
                column: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResidenceHalls_Code",
                table: "ResidenceHalls");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ResidenceHalls");
        }
    }
}
