using Microsoft.EntityFrameworkCore.Migrations;
using PorslineClone.Infrastructure.Services;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <summary>حذف ستون‌های اشتباه/تکراری و ایندکس‌های پرکاربرد (idempotent).</summary>
    public partial class fix_schema_redundancy_and_indexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // فقط پاکسازی ستون‌های اشتباه؛ ایندکس‌ها در sync_ef_model_indexes (idempotent)
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NOT NULL
                BEGIN
                    DECLARE @dfFormAction nvarchar(200);
                    SELECT @dfFormAction = d.name
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID(N'[dbo].[FormWorkflowTemplates]')
                      AND c.name = N'ActionAssigneeUserIdsJson';
                    IF @dfFormAction IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[FormWorkflowTemplates] DROP CONSTRAINT [' + @dfFormAction + N']');
                    ALTER TABLE [dbo].[FormWorkflowTemplates] DROP COLUMN [ActionAssigneeUserIdsJson];
                END
                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionKey') IS NOT NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] DROP COLUMN [ActionDirectionKey];
                IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionLabel') IS NOT NULL
                    ALTER TABLE [dbo].[FormWorkflowTemplates] DROP COLUMN [ActionDirectionLabel];
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down فقط ایندکس‌ها — ستون اشتباه FormWorkflowTemplates عمداً برنمی‌گردد.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissionApprovalLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissionApprovalLinks]'))
                    DROP INDEX [IX_FormSubmissionApprovalLinks_AssigneeUserId_IsActive] ON [dbo].[FormSubmissionApprovalLinks];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InboxMessages_UserId_IsArchived_IsRead' AND object_id = OBJECT_ID(N'[dbo].[InboxMessages]'))
                    DROP INDEX [IX_InboxMessages_UserId_IsArchived_IsRead] ON [dbo].[InboxMessages];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Forms_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Forms]'))
                    DROP INDEX [IX_Forms_WorkflowTemplateId] ON [dbo].[Forms];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
                    DROP INDEX [IX_FormSubmissions_WorkflowTemplateId] ON [dbo].[FormSubmissions];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_Status_PostApproval' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
                    DROP INDEX [IX_FormSubmissions_Status_PostApproval] ON [dbo].[FormSubmissions];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractActionLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[ContractActionLinks]'))
                    DROP INDEX [IX_ContractActionLinks_AssigneeUserId_IsActive] ON [dbo].[ContractActionLinks];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractApprovalLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[ContractApprovalLinks]'))
                    DROP INDEX [IX_ContractApprovalLinks_AssigneeUserId_IsActive] ON [dbo].[ContractApprovalLinks];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_CreatedByUserId_Status' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    DROP INDEX [IX_Contracts_CreatedByUserId_Status] ON [dbo].[Contracts];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_Approved_PostApproval' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    DROP INDEX [IX_Contracts_Approved_PostApproval] ON [dbo].[Contracts];
                """);
        }
    }
}
