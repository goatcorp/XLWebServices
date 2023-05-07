using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XLWebServices.Data.Migrations
{
    /// <inheritdoc />
    public partial class PluginVersionStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiffLinesAdded",
                table: "PluginVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiffLinesRemoved",
                table: "PluginVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInitialRelease",
                table: "PluginVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TimeToMerge",
                table: "PluginVersions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiffLinesAdded",
                table: "PluginVersions");

            migrationBuilder.DropColumn(
                name: "DiffLinesRemoved",
                table: "PluginVersions");

            migrationBuilder.DropColumn(
                name: "IsInitialRelease",
                table: "PluginVersions");

            migrationBuilder.DropColumn(
                name: "TimeToMerge",
                table: "PluginVersions");
        }
    }
}
