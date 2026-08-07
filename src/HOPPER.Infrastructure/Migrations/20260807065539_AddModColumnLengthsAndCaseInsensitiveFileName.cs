using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HOPPER.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModColumnLengthsAndCaseInsensitiveFileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Mods" SET
                    "FileName"    = left("FileName", 255),
                    "Sha1"        = left("Sha1", 40),
                    "Sha512"      = left("Sha512", 128),
                    "UploadedBy"  = left("UploadedBy", 200),
                    "ProjectId"   = left("ProjectId", 64),
                    "VersionId"   = left("VersionId", 64),
                    "ProjectName" = left("ProjectName", 255),
                    "DownloadUrl" = left("DownloadUrl", 2048)
                WHERE length("FileName") > 255
                   OR length("Sha1") > 40
                   OR length("Sha512") > 128
                   OR length("UploadedBy") > 200
                   OR length("ProjectId") > 64
                   OR length("VersionId") > 64
                   OR length("ProjectName") > 255
                   OR length("DownloadUrl") > 2048;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Mods_ServerId_FileName",
                table: "Mods");

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "Mods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UploadedBy",
                table: "Mods",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sha512",
                table: "Mods",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sha256",
                table: "Mods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Sha1",
                table: "Mods",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProjectName",
                table: "Mods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProjectId",
                table: "Mods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Mods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DownloadUrl",
                table: "Mods",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                DELETE FROM "Mods" m USING "Mods" keep
                WHERE m."ServerId" = keep."ServerId"
                  AND lower(m."FileName") = lower(keep."FileName")
                  AND (keep."CreatedAt", keep."Id") < (m."CreatedAt", m."Id");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_Mods_ServerId_FileNameLower"
                ON "Mods" ("ServerId", lower("FileName"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Mods_ServerId_FileNameLower";""");

            migrationBuilder.AlterColumn<string>(
                name: "VersionId",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UploadedBy",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sha512",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sha256",
                table: "Mods",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Sha1",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProjectName",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProjectId",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Mods",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "DownloadUrl",
                table: "Mods",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mods_ServerId_FileName",
                table: "Mods",
                columns: new[] { "ServerId", "FileName" },
                unique: true);
        }
    }
}
