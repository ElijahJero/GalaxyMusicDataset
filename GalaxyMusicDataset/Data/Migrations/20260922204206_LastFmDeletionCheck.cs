using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalaxyMusicDataset.Data.Migrations
{
    /// <inheritdoc />
    public partial class LastFmDeletionCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeletionCheckPaused",
                table: "SyncStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDeletionCheckUtc",
                table: "SyncStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SyncStates",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletionCheckPaused", "LastDeletionCheckUtc" },
                values: new object[] { false, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletionCheckPaused",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "LastDeletionCheckUtc",
                table: "SyncStates");
        }
    }
}
