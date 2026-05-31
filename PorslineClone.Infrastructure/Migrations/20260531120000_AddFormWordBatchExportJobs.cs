using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddFormWordBatchExportJobs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FormWordBatchExportJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SubmissionIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ImageOverridesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                TotalCount = table.Column<int>(type: "int", nullable: false),
                ProcessedCount = table.Column<int>(type: "int", nullable: false),
                ZipFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ZipFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                HangfireJobId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FormWordBatchExportJobs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FormWordBatchExportJobs_CreatedAtUtc",
            table: "FormWordBatchExportJobs",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_FormWordBatchExportJobs_CreatedByUserId",
            table: "FormWordBatchExportJobs",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_FormWordBatchExportJobs_Status",
            table: "FormWordBatchExportJobs",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FormWordBatchExportJobs");
    }
}
