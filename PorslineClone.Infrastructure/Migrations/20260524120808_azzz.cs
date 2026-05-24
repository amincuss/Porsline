using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azzz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WorkflowValidityReminderSmsEnabled",
                table: "SmsSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowValiditySuspensionDelayDays",
                table: "SmsSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowValiditySuspensionDelayHours",
                table: "SmsSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowValidityDays",
                table: "ContractWorkflowTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowValidityHours",
                table: "ContractWorkflowTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SuspendedPendingUserId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowIncompleteNote",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowIncompleteTerminatedAtUtc",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowValidityEndsAtUtc",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowValidityReminderSentAtUtc",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SmsSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "WorkflowValidityReminderSmsEnabled", "WorkflowValiditySuspensionDelayDays", "WorkflowValiditySuspensionDelayHours" },
                values: new object[] { false, 0, 24 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkflowValidityReminderSmsEnabled",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "WorkflowValiditySuspensionDelayDays",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "WorkflowValiditySuspensionDelayHours",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "WorkflowValidityDays",
                table: "ContractWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "WorkflowValidityHours",
                table: "ContractWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "SuspendedPendingUserId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "WorkflowIncompleteNote",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "WorkflowIncompleteTerminatedAtUtc",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "WorkflowValidityEndsAtUtc",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "WorkflowValidityReminderSentAtUtc",
                table: "Contracts");
        }
    }
}
