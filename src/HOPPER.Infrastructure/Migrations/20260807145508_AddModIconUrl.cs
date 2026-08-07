using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModIconUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconUrl",
                table: "Mods",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IconUrl",
                table: "Mods");
        }
    }
}
