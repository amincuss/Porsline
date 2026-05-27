using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class sss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This column may already be created by DatabaseSchemaPatcher in some environments.
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.SmsSettings', 'FormWorkflowStartedResponderSmsEnabled') IS NULL
BEGIN
    ALTER TABLE [dbo].[SmsSettings] ADD [FormWorkflowStartedResponderSmsEnabled] bit NOT NULL
        CONSTRAINT [DF_SmsSettings_FormWorkflowStartedResponderSmsEnabled] DEFAULT (1);
END
");

            migrationBuilder.CreateTable(
                name: "FormActionLinks",
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
                    table.PrimaryKey("PK_FormActionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormActionLinks_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "SmsSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "FormWorkflowStartedResponderSmsEnabled",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormActionLinks_Code",
                table: "FormActionLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormActionLinks_FormSubmissionId_AssigneeUserId_IsActive",
                table: "FormActionLinks",
                columns: new[] { "FormSubmissionId", "AssigneeUserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormActionLinks");

            migrationBuilder.DropColumn(
                name: "FormWorkflowStartedResponderSmsEnabled",
                table: "SmsSettings");
        }
    }
}
