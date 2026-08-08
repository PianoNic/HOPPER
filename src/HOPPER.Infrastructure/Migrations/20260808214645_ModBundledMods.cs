using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModBundledMods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "BundledMods",
                table: "Mods",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BundledMods",
                table: "Mods");
        }
    }
}
