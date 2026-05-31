using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vabsssتن : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentVersionTexts",
                columns: table => new
                {
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CharCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersionTexts", x => x.DocumentVersionId);
                    table.ForeignKey(
                        name: "FK_DocumentVersionTexts_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersionTexts_DocumentId",
                table: "DocumentVersionTexts",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersionTexts_ProcessingStatus_UpdatedAtUtc",
                table: "DocumentVersionTexts",
                columns: new[] { "ProcessingStatus", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentVersionTexts");
        }
    }
}
