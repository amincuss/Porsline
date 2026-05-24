using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class xczz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // فقط PostApprovalJson روی پاسخ فرم — ستون‌های اقدام فقط روی ContractWorkflowTemplates (مایگریشن klopsl)
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'PostApprovalJson') IS NULL
                    ALTER TABLE [dbo].[FormSubmissions] ADD [PostApprovalJson] nvarchar(max) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'PostApprovalJson') IS NOT NULL
                    ALTER TABLE [dbo].[FormSubmissions] DROP COLUMN [PostApprovalJson];
                """);
        }
    }
}
