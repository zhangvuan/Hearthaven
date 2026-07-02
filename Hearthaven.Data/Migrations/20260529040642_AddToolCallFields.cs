using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthaven.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddToolCallFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupId",
                table: "Messages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningContent",
                table: "Messages",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallId",
                table: "Messages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallsJson",
                table: "Messages",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReasoningContent",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolCallId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolCallsJson",
                table: "Messages");
        }
    }
}
