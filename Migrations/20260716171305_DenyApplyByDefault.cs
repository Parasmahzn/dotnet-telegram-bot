using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroShareBot.Migrations
{
    /// <inheritdoc />
    public partial class DenyApplyByDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsApplyAllowed",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);

            // Reset existing rows too — the prior migration's DEFAULT TRUE already backfilled them.
            migrationBuilder.Sql("UPDATE `Users` SET `IsApplyAllowed` = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsApplyAllowed",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: false);
        }
    }
}
