using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class VgmdbAlbumFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VgmdbAlbumId",
                table: "Tracks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogNumber",
                table: "Albums",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "Albums",
                type: "TEXT",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VgmdbAlbumId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CatalogNumber",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "Classification",
                table: "Albums");
        }
    }
}
