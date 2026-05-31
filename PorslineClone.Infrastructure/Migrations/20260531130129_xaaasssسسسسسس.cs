using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class xaaasssسسسسسس : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedDekBase64",
                table: "DocumentVersions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "DocumentVersions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileNonceBase64",
                table: "DocumentVersions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEncrypted",
                table: "DocumentVersions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_IsEncrypted_EncryptionKeyId",
                table: "DocumentVersions",
                columns: new[] { "IsEncrypted", "EncryptionKeyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_IsEncrypted_EncryptionKeyId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "EncryptedDekBase64",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "FileNonceBase64",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "IsEncrypted",
                table: "DocumentVersions");
        }
    }
}
