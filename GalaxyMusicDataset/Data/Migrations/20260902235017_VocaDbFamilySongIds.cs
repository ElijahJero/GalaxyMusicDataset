using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class VocaDbFamilySongIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TouhouDbSongId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtaiteDbSongId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VocaDbSongId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TouhouDbSongId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "UtaiteDbSongId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "VocaDbSongId",
                table: "Tracks");
        }
    }
}
