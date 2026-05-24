using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class gf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: DatabaseSchemaPatcher may have added these columns before EF migration runs.
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderDelayDays') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderDelayDays] int NOT NULL
                        CONSTRAINT [DF_SmsSettings_ApprovalReminderDelayDays] DEFAULT (0);

                IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderDelayHours') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderDelayHours] int NOT NULL
                        CONSTRAINT [DF_SmsSettings_ApprovalReminderDelayHours] DEFAULT (24);

                IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_ApprovalReminderSmsEnabled] DEFAULT (0);

                IF COL_LENGTH('dbo.SmsSettings', 'ContractAmendmentAssigneeSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ContractAmendmentAssigneeSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_ContractAmendmentAssigneeSmsEnabled] DEFAULT (1);

                IF COL_LENGTH('dbo.SmsSettings', 'ContractAmendmentReturnToRejecterSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ContractAmendmentReturnToRejecterSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_ContractAmendmentReturnToRejecterSmsEnabled] DEFAULT (1);

                IF COL_LENGTH('dbo.SmsSettings', 'ContractRejectionNotifySmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [ContractRejectionNotifySmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_ContractRejectionNotifySmsEnabled] DEFAULT (1);

                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL
                        CONSTRAINT [DF_FormWorkflowTemplates_ActionAssignees] DEFAULT ('[]');

                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionKey') IS NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionDirectionKey] nvarchar(80) NULL;

                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionLabel') IS NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionDirectionLabel] nvarchar(200) NULL;

                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'CanvasLayoutJson') IS NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [CanvasLayoutJson] nvarchar(500) NULL;

                IF COL_LENGTH('dbo.FormSubmissionApprovalLinks', 'ReminderSmsSentAtUtc') IS NULL
                    ALTER TABLE [dbo].[FormSubmissionApprovalLinks] ADD [ReminderSmsSentAtUtc] datetime2 NULL;

                IF COL_LENGTH('dbo.ContractVersions', 'IsAmendedVersion') IS NULL
                    ALTER TABLE [dbo].[ContractVersions] ADD [IsAmendedVersion] bit NOT NULL
                        CONSTRAINT [DF_ContractVersions_IsAmendedVersion] DEFAULT (0);

                IF COL_LENGTH('dbo.Contracts', 'AmendmentJson') IS NULL
                    ALTER TABLE [dbo].[Contracts] ADD [AmendmentJson] nvarchar(max) NULL;

                IF COL_LENGTH('dbo.Contracts', 'WorkflowEventsJson') IS NULL
                    ALTER TABLE [dbo].[Contracts] ADD [WorkflowEventsJson] nvarchar(max) NULL;

                IF COL_LENGTH('dbo.ContractApprovalLinks', 'ReminderSmsSentAtUtc') IS NULL
                    ALTER TABLE [dbo].[ContractApprovalLinks] ADD [ReminderSmsSentAtUtc] datetime2 NULL;
                """);

            migrationBuilder.UpdateData(
                table: "SmsSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ApprovalReminderDelayDays", "ApprovalReminderDelayHours", "ApprovalReminderSmsEnabled", "ContractAmendmentAssigneeSmsEnabled", "ContractAmendmentReturnToRejecterSmsEnabled", "ContractRejectionNotifySmsEnabled" },
                values: new object[] { 0, 24, false, true, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderSmsSentAtUtc",
                table: "ContractApprovalLinks");

            migrationBuilder.DropColumn(
                name: "WorkflowEventsJson",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "AmendmentJson",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "IsAmendedVersion",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "ReminderSmsSentAtUtc",
                table: "FormSubmissionApprovalLinks");

            migrationBuilder.DropColumn(
                name: "CanvasLayoutJson",
                table: "FormWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "ActionDirectionLabel",
                table: "FormWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "ActionDirectionKey",
                table: "FormWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "ActionAssigneeUserIdsJson",
                table: "FormWorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "ContractRejectionNotifySmsEnabled",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "ContractAmendmentReturnToRejecterSmsEnabled",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "ContractAmendmentAssigneeSmsEnabled",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "ApprovalReminderSmsEnabled",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "ApprovalReminderDelayHours",
                table: "SmsSettings");

            migrationBuilder.DropColumn(
                name: "ApprovalReminderDelayDays",
                table: "SmsSettings");
        }
    }
}
