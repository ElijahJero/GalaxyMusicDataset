using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiScrobbleProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Scrobbles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Scrobbles_Source_UnixTimestamp",
                table: "Scrobbles",
                columns: new[] { "Source", "UnixTimestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scrobbles_Source_UnixTimestamp",
                table: "Scrobbles");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Scrobbles");
        }
    }
}
