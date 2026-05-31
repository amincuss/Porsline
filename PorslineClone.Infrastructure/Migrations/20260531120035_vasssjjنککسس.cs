using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations;

/// <inheritdoc />
/// <summary>
/// Idempotent migration split into separate SQL batches (SQL Server compiles the whole batch before running).
/// </summary>
public partial class vasssjjنککسس : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF COL_LENGTH('dbo.Documents', 'WorkflowStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStatus] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowStatus_Mig] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowName') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowName] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.Documents', 'StepsJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [StepsJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'CurrentStepOrder') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [CurrentStepOrder] int NOT NULL
                    CONSTRAINT [DF_Documents_CurrentStepOrder_Mig] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'PostApprovalJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [PostApprovalJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunCycle') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunCycle] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowRunCycle_Mig] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunsHistoryJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunsHistoryJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRejectionJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRejectionJson] nvarchar(max) NULL;
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF COL_LENGTH('dbo.Documents', 'ExpiresAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ExpiresAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'RetentionPolicyId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [RetentionPolicyId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'ArchiveTier') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchiveTier] int NOT NULL
                    CONSTRAINT [DF_Documents_ArchiveTier_Mig] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LifecycleStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LifecycleStatus] int NOT NULL
                    CONSTRAINT [DF_Documents_LifecycleStatus_Mig] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'IsArchived') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsArchived] bit NOT NULL
                    CONSTRAINT [DF_Documents_IsArchived_Mig] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'ArchivedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchivedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHold') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHold] bit NOT NULL
                    CONSTRAINT [DF_Documents_LegalHold_Mig] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LegalHoldReason') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldReason] nvarchar(500) NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldByUserId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldByUserId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'IsObsolete') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsObsolete] bit NOT NULL
                    CONSTRAINT [DF_Documents_IsObsolete_Mig] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'ObsoleteAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ObsoleteAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'ObsoleteReason') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ObsoleteReason] nvarchar(500) NULL;
            IF COL_LENGTH('dbo.Documents', 'LifecycleWarningSentAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LifecycleWarningSentAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'ScheduledArchiveAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ScheduledArchiveAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'ScheduledDeleteAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ScheduledDeleteAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LongTermRetention') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LongTermRetention] bit NOT NULL
                    CONSTRAINT [DF_Documents_LongTermRetention_Mig] DEFAULT(0);
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[DocumentWorkflowTemplates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentWorkflowTemplates] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [StepsJson] nvarchar(max) NOT NULL,
                    [ActionDirectionKey] nvarchar(80) NULL,
                    [ActionDirectionLabel] nvarchar(200) NULL,
                    [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL CONSTRAINT [DF_DocumentWorkflowTemplates_ActionAssignees_Mig] DEFAULT ('[]'),
                    [CanvasLayoutJson] nvarchar(500) NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedByUserId] uniqueidentifier NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentWorkflowTemplates] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentWorkflowTemplates_Name] ON [dbo].[DocumentWorkflowTemplates]([Name]);
                CREATE INDEX [IX_DocumentWorkflowTemplates_IsActive_CreatedAtUtc] ON [dbo].[DocumentWorkflowTemplates]([IsActive], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentRetentionPolicies]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentRetentionPolicies] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [Description] nvarchar(500) NULL,
                    [CategoryMatch] nvarchar(120) NULL,
                    [AccessLevelMatch] int NULL,
                    [ArchiveAfterDays] int NULL,
                    [MoveToColdAfterDays] int NULL,
                    [DeleteAfterDays] int NULL,
                    [ExpirationWarningDays] int NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_ExpirationWarningDays_Mig] DEFAULT(30),
                    [LongTermRetention] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_LongTermRetention_Mig] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_IsActive_Mig] DEFAULT(1),
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_IsDefault_Mig] DEFAULT(0),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_SortOrder_Mig] DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentRetentionPolicies] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentRetentionPolicies_Name] ON [dbo].[DocumentRetentionPolicies]([Name]);
                CREATE INDEX [IX_DocumentRetentionPolicies_IsActive_IsDefault_SortOrder] ON [dbo].[DocumentRetentionPolicies]([IsActive], [IsDefault], [SortOrder]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentLifecycleSettings] (
                    [Id] uniqueidentifier NOT NULL,
                    [DefaultRetentionPolicyId] uniqueidentifier NULL,
                    [AutoProcessEnabled] bit NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_AutoProcessEnabled_Mig] DEFAULT(1),
                    [DefaultExpirationWarningDays] int NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_DefaultExpirationWarningDays_Mig] DEFAULT(30),
                    [ProcessIntervalHours] int NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_ProcessIntervalHours_Mig] DEFAULT(6),
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentLifecycleSettings] PRIMARY KEY ([Id])
                );
            END

            IF OBJECT_ID(N'[dbo].[DocumentApprovalLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentApprovalLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [AssigneeUserId] uniqueidentifier NOT NULL,
                    [Code] nvarchar(32) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ExpiresAtUtc] datetime2 NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [ReminderSmsSentAtUtc] datetime2 NULL,
                    CONSTRAINT [PK_DocumentApprovalLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentApprovalLinks_Documents_DocumentId]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_DocumentApprovalLinks_Code] ON [dbo].[DocumentApprovalLinks]([Code]);
                CREATE INDEX [IX_DocumentApprovalLinks_DocumentId_AssigneeUserId_IsActive]
                    ON [dbo].[DocumentApprovalLinks]([DocumentId], [AssigneeUserId], [IsActive]);
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_IsArchived] ON [dbo].[Documents]([IsArchived]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_LegalHold' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_LegalHold] ON [dbo].[Documents]([LegalHold]) WHERE [LegalHold] = 1;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_LifecycleStatus_IsArchived_UpdatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_LifecycleStatus_IsArchived_UpdatedAtUtc] ON [dbo].[Documents]([LifecycleStatus], [IsArchived], [UpdatedAtUtc]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_RetentionPolicyId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_RetentionPolicyId] ON [dbo].[Documents]([RetentionPolicyId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ScheduledArchiveAtUtc_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_ScheduledArchiveAtUtc_IsArchived] ON [dbo].[Documents]([ScheduledArchiveAtUtc], [IsArchived])
                    WHERE [ScheduledArchiveAtUtc] IS NOT NULL AND [IsArchived] = 0;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ScheduledDeleteAtUtc_LegalHold_LongTermRetention' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_ScheduledDeleteAtUtc_LegalHold_LongTermRetention] ON [dbo].[Documents]([ScheduledDeleteAtUtc], [LegalHold], [LongTermRetention])
                    WHERE [ScheduledDeleteAtUtc] IS NOT NULL AND [LegalHold] = 0 AND [LongTermRetention] = 0;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_WorkflowStatus_CurrentStepOrder' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_WorkflowStatus_CurrentStepOrder] ON [dbo].[Documents]([WorkflowStatus], [CurrentStepOrder]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_WorkflowTemplateId] ON [dbo].[Documents]([WorkflowTemplateId]) WHERE [WorkflowTemplateId] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentRetentionPolicies_RetentionPolicyId')
               AND COL_LENGTH('dbo.Documents', 'RetentionPolicyId') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentRetentionPolicies]', N'U') IS NOT NULL
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentRetentionPolicies_RetentionPolicyId]
                    FOREIGN KEY ([RetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentWorkflowTemplates_WorkflowTemplateId')
               AND COL_LENGTH('dbo.Documents', 'WorkflowTemplateId') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentWorkflowTemplates]', N'U') IS NOT NULL
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentWorkflowTemplates_WorkflowTemplateId]
                    FOREIGN KEY ([WorkflowTemplateId]) REFERENCES [dbo].[DocumentWorkflowTemplates]([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentLifecycleSettings_DocumentRetentionPolicies_DefaultRetentionPolicyId')
               AND OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentRetentionPolicies]', N'U') IS NOT NULL
                ALTER TABLE [dbo].[DocumentLifecycleSettings] ADD CONSTRAINT [FK_DocumentLifecycleSettings_DocumentRetentionPolicies_DefaultRetentionPolicyId]
                    FOREIGN KEY ([DefaultRetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentLifecycleSettings_DefaultRetentionPolicyId' AND object_id = OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]'))
                CREATE INDEX [IX_DocumentLifecycleSettings_DefaultRetentionPolicyId] ON [dbo].[DocumentLifecycleSettings]([DefaultRetentionPolicyId]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
