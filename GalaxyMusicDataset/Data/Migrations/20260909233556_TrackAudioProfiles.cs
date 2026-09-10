using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackAudioProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackAudioProfiles",
                columns: table => new
                {
                    TrackId = table.Column<long>(type: "INTEGER", nullable: false),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Bpm = table.Column<double>(type: "REAL", nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Scale = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    KeyStrength = table.Column<double>(type: "REAL", nullable: true),
                    Loudness = table.Column<double>(type: "REAL", nullable: true),
                    Danceability = table.Column<double>(type: "REAL", nullable: true),
                    Acoustic = table.Column<double>(type: "REAL", nullable: true),
                    Electronic = table.Column<double>(type: "REAL", nullable: true),
                    Voice = table.Column<double>(type: "REAL", nullable: true),
                    Instrumental = table.Column<double>(type: "REAL", nullable: true),
                    Tonal = table.Column<double>(type: "REAL", nullable: true),
                    Timbre = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TimbreBright = table.Column<double>(type: "REAL", nullable: true),
                    Approachability = table.Column<double>(type: "REAL", nullable: true),
                    Engagement = table.Column<double>(type: "REAL", nullable: true),
                    MoodHappy = table.Column<double>(type: "REAL", nullable: true),
                    MoodSad = table.Column<double>(type: "REAL", nullable: true),
                    MoodAggressive = table.Column<double>(type: "REAL", nullable: true),
                    MoodRelaxed = table.Column<double>(type: "REAL", nullable: true),
                    MoodParty = table.Column<double>(type: "REAL", nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAudioProfiles", x => x.TrackId);
                    table.ForeignKey(
                        name: "FK_TrackAudioProfiles_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackAudioLabels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackAudioLabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackAudioLabels_TrackAudioProfiles_TrackId",
                        column: x => x.TrackId,
                        principalTable: "TrackAudioProfiles",
                        principalColumn: "TrackId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackAudioLabels_Kind_Name",
                table: "TrackAudioLabels",
                columns: new[] { "Kind", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackAudioLabels_TrackId_Kind_Name",
                table: "TrackAudioLabels",
                columns: new[] { "TrackId", "Kind", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackAudioLabels");

            migrationBuilder.DropTable(
                name: "TrackAudioProfiles");
        }
    }
}
