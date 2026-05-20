using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class zzzk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DispatchLinkId",
                table: "FormSubmissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponderId",
                table: "FormSubmissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowName",
                table: "FormSubmissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowScheduledStartAtUtc",
                table: "FormSubmissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowStartedAtUtc",
                table: "FormSubmissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowTemplateId",
                table: "FormSubmissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowName",
                table: "Forms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowTemplateId",
                table: "Forms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormSubmissionApprovalLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionApprovalLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormSubmissionApprovalLinks_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormWorkflowTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StepsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormWorkflowTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_DispatchLinkId",
                table: "FormSubmissions",
                column: "DispatchLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_ResponderId",
                table: "FormSubmissions",
                column: "ResponderId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_WorkflowTemplateId",
                table: "FormSubmissions",
                column: "WorkflowTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Forms_WorkflowTemplateId",
                table: "Forms",
                column: "WorkflowTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionApprovalLinks_Code",
                table: "FormSubmissionApprovalLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionApprovalLinks_FormSubmissionId_AssigneeUserId_IsActive",
                table: "FormSubmissionApprovalLinks",
                columns: new[] { "FormSubmissionId", "AssigneeUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FormWorkflowTemplates_IsActive_CreatedAtUtc",
                table: "FormWorkflowTemplates",
                columns: new[] { "IsActive", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormWorkflowTemplates_Name",
                table: "FormWorkflowTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Forms_FormWorkflowTemplates_WorkflowTemplateId",
                table: "Forms",
                column: "WorkflowTemplateId",
                principalTable: "FormWorkflowTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FormSubmissions_FormWorkflowTemplates_WorkflowTemplateId",
                table: "FormSubmissions",
                column: "WorkflowTemplateId",
                principalTable: "FormWorkflowTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Forms_FormWorkflowTemplates_WorkflowTemplateId",
                table: "Forms");

            migrationBuilder.DropForeignKey(
                name: "FK_FormSubmissions_FormWorkflowTemplates_WorkflowTemplateId",
                table: "FormSubmissions");

            migrationBuilder.DropTable(
                name: "FormSubmissionApprovalLinks");

            migrationBuilder.DropTable(
                name: "FormWorkflowTemplates");

            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_DispatchLinkId",
                table: "FormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_ResponderId",
                table: "FormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_WorkflowTemplateId",
                table: "FormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_Forms_WorkflowTemplateId",
                table: "Forms");

            migrationBuilder.DropColumn(
                name: "DispatchLinkId",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "ResponderId",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowName",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowScheduledStartAtUtc",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowStartedAtUtc",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowTemplateId",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowName",
                table: "Forms");

            migrationBuilder.DropColumn(
                name: "WorkflowTemplateId",
                table: "Forms");
        }
    }
}
