using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthaven.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Sessions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "Sessions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingDirectory",
                table: "Sessions",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "WorkingDirectory",
                table: "Sessions");
        }
    }
}
