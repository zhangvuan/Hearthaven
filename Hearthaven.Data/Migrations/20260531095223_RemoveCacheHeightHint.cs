using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthaven.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCacheHeightHint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheHeightHint",
                table: "Messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CacheHeightHint",
                table: "Messages",
                type: "REAL",
                nullable: true);
        }
    }
}
