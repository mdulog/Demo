using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pacevite.Api.Migrations
{
    /// <inheritdoc />
    public partial class EventsKeysetPaginationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_UserId_EventDate",
                table: "Events");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId_EventDate_Id",
                table: "Events",
                columns: new[] { "UserId", "EventDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_UserId_EventDate_Id",
                table: "Events");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId_EventDate",
                table: "Events",
                columns: new[] { "UserId", "EventDate" });
        }
    }
}
