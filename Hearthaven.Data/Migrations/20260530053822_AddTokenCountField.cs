using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthaven.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenCountField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TokenCount",
                table: "Messages",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenCount",
                table: "Messages");
        }
    }
}
