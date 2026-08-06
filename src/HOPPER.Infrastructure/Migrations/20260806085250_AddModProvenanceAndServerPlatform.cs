using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModProvenanceAndServerPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Loader",
                table: "Servers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LoaderVersion",
                table: "Servers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MinecraftVersion",
                table: "Servers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadUrl",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha1",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha512",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Mods",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VersionId",
                table: "Mods",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mods_ServerId_ProjectId",
                table: "Mods",
                columns: new[] { "ServerId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mods_ServerId_ProjectId",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "Loader",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "LoaderVersion",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "MinecraftVersion",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "DownloadUrl",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "Sha1",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "Sha512",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "VersionId",
                table: "Mods");
        }
    }
}
