using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModSide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Side",
                table: "Mods",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Side",
                table: "Mods");
        }
    }
}
