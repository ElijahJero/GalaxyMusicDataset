using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScrobbleTrackTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scrobbles_TrackId_UnixTimestamp",
                table: "Scrobbles",
                columns: new[] { "TrackId", "UnixTimestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scrobbles_TrackId_UnixTimestamp",
                table: "Scrobbles");
        }
    }
}
