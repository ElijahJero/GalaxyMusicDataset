using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackMetadataFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscogsReleaseId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Isrc",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicVideoUrl",
                table: "Tracks",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Tracks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TheAudioDbTrackId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Artists",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscogsReleaseId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Isrc",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "MusicVideoUrl",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "TheAudioDbTrackId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Artists");
        }
    }
}
