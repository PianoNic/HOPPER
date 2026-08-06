using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModModIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "ModIds",
                table: "Mods",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModIds",
                table: "Mods");
        }
    }
}
