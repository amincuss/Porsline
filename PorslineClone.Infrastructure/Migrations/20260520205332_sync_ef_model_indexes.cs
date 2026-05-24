using Microsoft.EntityFrameworkCore.Migrations;
using PorslineClone.Infrastructure.Services;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class sync_ef_model_indexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SchemaCleanupSql.CleanupAndIndexes);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'PostApprovalJson') IS NULL
                    ALTER TABLE [dbo].[FormSubmissions] ADD [PostApprovalJson] nvarchar(max) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InboxMessages_UserId_IsArchived_IsRead_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[InboxMessages]'))
                    CREATE NONCLUSTERED INDEX [IX_InboxMessages_UserId_IsArchived_IsRead_CreatedAtUtc]
                        ON [dbo].[InboxMessages]([UserId], [IsArchived], [IsRead], [CreatedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
                    CREATE NONCLUSTERED INDEX [IX_FormSubmissions_WorkflowTemplateId]
                        ON [dbo].[FormSubmissions]([WorkflowTemplateId])
                        WHERE [WorkflowTemplateId] IS NOT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Forms_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Forms]'))
                    CREATE NONCLUSTERED INDEX [IX_Forms_WorkflowTemplateId]
                        ON [dbo].[Forms]([WorkflowTemplateId])
                        WHERE [WorkflowTemplateId] IS NOT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_CreatedByUserId_Status' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    CREATE NONCLUSTERED INDEX [IX_Contracts_CreatedByUserId_Status]
                        ON [dbo].[Contracts]([CreatedByUserId], [Status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_Status_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    CREATE NONCLUSTERED INDEX [IX_Contracts_Status_IsArchived]
                        ON [dbo].[Contracts]([Status], [IsArchived])
                        WHERE [Status] = 3 AND [IsArchived] = 0 AND [PostApprovalJson] IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_Status_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    DROP INDEX [IX_Contracts_Status_IsArchived] ON [dbo].[Contracts];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_CreatedByUserId_Status' AND object_id = OBJECT_ID(N'[dbo].[Contracts]'))
                    DROP INDEX [IX_Contracts_CreatedByUserId_Status] ON [dbo].[Contracts];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Forms_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Forms]'))
                    DROP INDEX [IX_Forms_WorkflowTemplateId] ON [dbo].[Forms];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FormSubmissions_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
                    DROP INDEX [IX_FormSubmissions_WorkflowTemplateId] ON [dbo].[FormSubmissions];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InboxMessages_UserId_IsArchived_IsRead_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[InboxMessages]'))
                    DROP INDEX [IX_InboxMessages_UserId_IsArchived_IsRead_CreatedAtUtc] ON [dbo].[InboxMessages];
                IF COL_LENGTH('dbo.FormSubmissions', 'PostApprovalJson') IS NOT NULL
                    ALTER TABLE [dbo].[FormSubmissions] DROP COLUMN [PostApprovalJson];
                """);
        }
    }
}
