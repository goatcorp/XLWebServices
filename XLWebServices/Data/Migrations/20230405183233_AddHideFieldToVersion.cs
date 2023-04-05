using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XLWebServices.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHideFieldToVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "PluginVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "PluginVersions");
        }
    }
}
