using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azzza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfFilePath",
                table: "ContractVersions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContractDocumentTemplateId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContractDocumentTemplateVersionId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfFilePath",
                table: "Contracts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateFieldValuesJson",
                table: "Contracts",
                type: "nvarchar(max)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractDocumentTemplateFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DesignerOrderJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OptionsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocumentTemplateFields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractDocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ActiveVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractDocumentTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DetectedPlaceholdersJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocumentTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractDocumentTemplateVersions_ContractDocumentTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ContractDocumentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ContractDocumentTemplateId",
                table: "Contracts",
                column: "ContractDocumentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_Key",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_SortOrder",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "TemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplates_ActiveVersionId",
                table: "ContractDocumentTemplates",
                column: "ActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplates_IsActive_CreatedAtUtc",
                table: "ContractDocumentTemplates",
                columns: new[] { "IsActive", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplates_Name",
                table: "ContractDocumentTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateVersions_TemplateId_VersionNumber",
                table: "ContractDocumentTemplateVersions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_ContractDocumentTemplates_ContractDocumentTemplateId",
                table: "Contracts",
                column: "ContractDocumentTemplateId",
                principalTable: "ContractDocumentTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateFields",
                column: "TemplateId",
                principalTable: "ContractDocumentTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId",
                table: "ContractDocumentTemplates",
                column: "ActiveVersionId",
                principalTable: "ContractDocumentTemplateVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_ContractDocumentTemplates_ContractDocumentTemplateId",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_ContractDocumentTemplateVersions_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateVersions");

            migrationBuilder.DropTable(
                name: "ContractDocumentTemplateFields");

            migrationBuilder.DropTable(
                name: "ContractDocumentTemplates");

            migrationBuilder.DropTable(
                name: "ContractDocumentTemplateVersions");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_ContractDocumentTemplateId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PdfFilePath",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "ContractDocumentTemplateId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ContractDocumentTemplateVersionId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PdfFilePath",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TemplateFieldValuesJson",
                table: "Contracts");
        }
    }
}
