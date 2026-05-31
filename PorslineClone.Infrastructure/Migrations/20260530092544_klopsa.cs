using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class klopsa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_CreatedByUserId",
                table: "Contracts");

            migrationBuilder.AddColumn<int>(
                name: "IndexStatus",
                table: "Contracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ContractTextIndexes",
                columns: table => new
                {
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExtractorVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContractVersionNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTextIndexes", x => x.ContractId);
                    table.ForeignKey(
                        name: "FK_ContractTextIndexes_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormFieldGroupTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFieldGroupTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractTextIndexes_ExtractedAt",
                table: "ContractTextIndexes",
                column: "ExtractedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FormFieldGroupTemplates_Name",
                table: "FormFieldGroupTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FormFieldGroupTemplates_UpdatedAtUtc",
                table: "FormFieldGroupTemplates",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractTextIndexes");

            migrationBuilder.DropTable(
                name: "FormFieldGroupTemplates");

            migrationBuilder.DropColumn(
                name: "IndexStatus",
                table: "Contracts");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CreatedByUserId",
                table: "Contracts",
                column: "CreatedByUserId");
        }
    }
}
