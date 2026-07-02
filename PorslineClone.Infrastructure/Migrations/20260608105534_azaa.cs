using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azaa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormDispatchGroupSendJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SkipWorkflow = table.Column<bool>(type: "bit", nullable: false),
                    SmsMessageMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomSmsBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    SentCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HangfireJobId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDispatchGroupSendJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDispatchGroupSendJobs_CreatedAtUtc",
                table: "FormDispatchGroupSendJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FormDispatchGroupSendJobs_CreatedByUserId",
                table: "FormDispatchGroupSendJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormDispatchGroupSendJobs_FormId",
                table: "FormDispatchGroupSendJobs",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_FormDispatchGroupSendJobs_GroupId",
                table: "FormDispatchGroupSendJobs",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FormDispatchGroupSendJobs_Status",
                table: "FormDispatchGroupSendJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormDispatchGroupSendJobs");
        }
    }
}
