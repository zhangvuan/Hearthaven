using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthaven.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSessionIdTimestampIndexWithSessionIdId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_SessionId_Timestamp",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SessionId_Id",
                table: "Messages",
                columns: new[] { "SessionId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_SessionId_Id",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SessionId_Timestamp",
                table: "Messages",
                columns: new[] { "SessionId", "Timestamp" });
        }
    }
}
