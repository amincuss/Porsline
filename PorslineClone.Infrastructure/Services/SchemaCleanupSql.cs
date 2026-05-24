namespace PorslineClone.Infrastructure.Services;

/// <summary>اسکریپت‌های idempotent پاکسازی ستون‌های تکراری و ایندکس‌های پرکاربرد.</summary>
internal static class SchemaCleanupSql
{
    internal const string CleanupAndIndexes = """
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

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_Approved_PostApproval' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
            DROP INDEX [IX_Contracts_Approved_PostApproval] ON [dbo].[Contracts];

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractApprovalLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[ContractApprovalLinks]'))
            CREATE NONCLUSTERED INDEX [IX_ContractApprovalLinks_AssigneeUserId_IsActive]
                ON [dbo].[ContractApprovalLinks]([AssigneeUserId], [IsActive])
                INCLUDE ([ContractId], [Code], [ExpiresAtUtc]);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractActionLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[ContractActionLinks]'))
            CREATE NONCLUSTERED INDEX [IX_ContractActionLinks_AssigneeUserId_IsActive]
                ON [dbo].[ContractActionLinks]([AssigneeUserId], [IsActive])
                INCLUDE ([ContractId], [Code], [ExpiresAtUtc]);

        IF COL_LENGTH('dbo.FormSubmissions', 'PostApprovalJson') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_Status_PostApproval' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
            CREATE NONCLUSTERED INDEX [IX_FormSubmissions_Status_PostApproval]
                ON [dbo].[FormSubmissions]([Status])
                INCLUDE ([FormId], [PostApprovalJson], [SubmittedAtUtc])
                WHERE [PostApprovalJson] IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
            CREATE NONCLUSTERED INDEX [IX_FormSubmissions_WorkflowTemplateId]
                ON [dbo].[FormSubmissions]([WorkflowTemplateId])
                WHERE [WorkflowTemplateId] IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Forms_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Forms]'))
            CREATE NONCLUSTERED INDEX [IX_Forms_WorkflowTemplateId]
                ON [dbo].[Forms]([WorkflowTemplateId])
                WHERE [WorkflowTemplateId] IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InboxMessages_UserId_IsArchived_IsRead' AND object_id = OBJECT_ID(N'[dbo].[InboxMessages]'))
            CREATE NONCLUSTERED INDEX [IX_InboxMessages_UserId_IsArchived_IsRead]
                ON [dbo].[InboxMessages]([UserId], [IsArchived], [IsRead], [CreatedAtUtc]);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissionApprovalLinks_AssigneeUserId_IsActive' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissionApprovalLinks]'))
            CREATE NONCLUSTERED INDEX [IX_FormSubmissionApprovalLinks_AssigneeUserId_IsActive]
                ON [dbo].[FormSubmissionApprovalLinks]([AssigneeUserId], [IsActive])
                INCLUDE ([FormSubmissionId], [Code], [ExpiresAtUtc]);
        """;
}
