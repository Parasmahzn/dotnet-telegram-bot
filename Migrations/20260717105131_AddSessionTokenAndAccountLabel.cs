using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroShareBot.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTokenAndAccountLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "LinkedAccounts",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SessionToken",
                table: "LinkedAccounts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SessionTokenExpiresAt",
                table: "LinkedAccounts",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "LinkedAccounts");

            migrationBuilder.DropColumn(
                name: "SessionToken",
                table: "LinkedAccounts");

            migrationBuilder.DropColumn(
                name: "SessionTokenExpiresAt",
                table: "LinkedAccounts");
        }
    }
}
