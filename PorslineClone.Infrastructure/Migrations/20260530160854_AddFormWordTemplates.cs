using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormWordTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormWordTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocxFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    DocxFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DetectedPlaceholdersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldMappingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignaturePlaceholderKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SignatureImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormWordTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormWordTemplates_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormSubmissionWordDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionWordDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormSubmissionWordDocuments_FormSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FormSubmissionWordDocuments_FormWordTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "FormWordTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWordDocuments_SubmissionId",
                table: "FormSubmissionWordDocuments",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWordDocuments_SubmissionId_TemplateId",
                table: "FormSubmissionWordDocuments",
                columns: new[] { "SubmissionId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWordDocuments_TemplateId",
                table: "FormSubmissionWordDocuments",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_FormWordTemplates_FormId",
                table: "FormWordTemplates",
                column: "FormId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormSubmissionWordDocuments");

            migrationBuilder.DropTable(
                name: "FormWordTemplates");
        }
    }
}
