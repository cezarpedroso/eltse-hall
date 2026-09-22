using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaDuty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDormSuiteSweepQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NeedsFollowUp",
                table: "DormSuiteSweeps",
                newName: "SmellsLikeMarijuanaOrAlcohol");

            migrationBuilder.RenameColumn(
                name: "HasTrashInCommonArea",
                table: "DormSuiteSweeps",
                newName: "IsCommonAreaClean");

            migrationBuilder.RenameColumn(
                name: "HasTrashInBathroom",
                table: "DormSuiteSweeps",
                newName: "IsBathroomClean");

            migrationBuilder.RenameColumn(
                name: "HasCommonAreaDamage",
                table: "DormSuiteSweeps",
                newName: "HasMoldOrLeak");

            migrationBuilder.RenameColumn(
                name: "HasBathroomIssue",
                table: "DormSuiteSweeps",
                newName: "AreToiletsAndSinksWorking");

            migrationBuilder.AddColumn<bool>(
                name: "AreShowersWorking",
                table: "DormSuiteSweeps",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE DormSuiteSweeps
                SET IsCommonAreaClean = CASE WHEN IsCommonAreaClean = 1 THEN 0 ELSE 1 END,
                    IsBathroomClean = CASE WHEN IsBathroomClean = 1 THEN 0 ELSE 1 END,
                    AreToiletsAndSinksWorking = CASE WHEN AreToiletsAndSinksWorking = 1 THEN 0 ELSE 1 END,
                    SmellsLikeMarijuanaOrAlcohol = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreShowersWorking",
                table: "DormSuiteSweeps");

            migrationBuilder.RenameColumn(
                name: "SmellsLikeMarijuanaOrAlcohol",
                table: "DormSuiteSweeps",
                newName: "NeedsFollowUp");

            migrationBuilder.RenameColumn(
                name: "IsCommonAreaClean",
                table: "DormSuiteSweeps",
                newName: "HasTrashInCommonArea");

            migrationBuilder.RenameColumn(
                name: "IsBathroomClean",
                table: "DormSuiteSweeps",
                newName: "HasTrashInBathroom");

            migrationBuilder.RenameColumn(
                name: "HasMoldOrLeak",
                table: "DormSuiteSweeps",
                newName: "HasCommonAreaDamage");

            migrationBuilder.RenameColumn(
                name: "AreToiletsAndSinksWorking",
                table: "DormSuiteSweeps",
                newName: "HasBathroomIssue");

            migrationBuilder.Sql("""
                UPDATE DormSuiteSweeps
                SET HasTrashInCommonArea = CASE WHEN HasTrashInCommonArea = 1 THEN 0 ELSE 1 END,
                    HasTrashInBathroom = CASE WHEN HasTrashInBathroom = 1 THEN 0 ELSE 1 END,
                    HasBathroomIssue = CASE WHEN HasBathroomIssue = 1 THEN 0 ELSE 1 END,
                    NeedsFollowUp = 0
                """);
        }
    }
}
