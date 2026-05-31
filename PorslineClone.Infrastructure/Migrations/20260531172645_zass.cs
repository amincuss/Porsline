using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class zass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: schema patcher may have already applied these columns before this migration runs.
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReferralSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReferralSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentApprovalReferralSmsEnabled_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayDays') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderDelayDays] int NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayDays_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayHours') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderDelayHours] int NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayHours_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderSmsEnabled_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentOwnerStepApprovalNotifySmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentOwnerStepApprovalNotifySmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentOwnerStepApprovalNotifySmsEnabled_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentPostApprovalAssigneeSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentPostApprovalAssigneeSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentPostApprovalAssigneeSmsEnabled_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowCompletedOwnerSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentWorkflowCompletedOwnerSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentWorkflowCompletedOwnerSmsEnabled_Mig] DEFAULT (0);
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowRejectedOwnerSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [DocumentWorkflowRejectedOwnerSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_DocumentWorkflowRejectedOwnerSmsEnabled_Mig] DEFAULT (0);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[DocumentSystemOrganizationalUnits] (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(120) NOT NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemOrganizationalUnits_IsActive_Mig] DEFAULT (1),
                        [CreatedByUserId] uniqueidentifier NOT NULL,
                        [CreatedAtUtc] datetime2 NOT NULL,
                        CONSTRAINT [PK_DocumentSystemOrganizationalUnits] PRIMARY KEY ([Id])
                    );
                END

                IF OBJECT_ID(N'[dbo].[DocumentSystemProjects]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[DocumentSystemProjects] (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(120) NOT NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemProjects_IsActive_Mig] DEFAULT (1),
                        [CreatedByUserId] uniqueidentifier NOT NULL,
                        [CreatedAtUtc] datetime2 NOT NULL,
                        CONSTRAINT [PK_DocumentSystemProjects] PRIMARY KEY ([Id])
                    );
                END

                IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('dbo.Documents', 'OrganizationalUnitId') IS NULL
                        ALTER TABLE [dbo].[Documents] ADD [OrganizationalUnitId] uniqueidentifier NULL;
                    IF COL_LENGTH('dbo.Documents', 'ProjectId') IS NULL
                        ALTER TABLE [dbo].[Documents] ADD [ProjectId] uniqueidentifier NULL;
                END

                IF OBJECT_ID(N'[dbo].[DocumentFolders]', N'U') IS NOT NULL
                   AND COL_LENGTH('dbo.DocumentFolders', 'Description') IS NULL
                    ALTER TABLE [dbo].[DocumentFolders] ADD [Description] nvarchar(500) NULL;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_OrganizationalUnitId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                    CREATE INDEX [IX_Documents_OrganizationalUnitId] ON [dbo].[Documents]([OrganizationalUnitId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ProjectId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                    CREATE INDEX [IX_Documents_ProjectId] ON [dbo].[Documents]([ProjectId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentSystemOrganizationalUnits_IsActive_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]'))
                    CREATE INDEX [IX_DocumentSystemOrganizationalUnits_IsActive_CreatedAtUtc]
                        ON [dbo].[DocumentSystemOrganizationalUnits]([IsActive], [CreatedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentSystemOrganizationalUnits_Name' AND object_id = OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]'))
                    CREATE UNIQUE INDEX [IX_DocumentSystemOrganizationalUnits_Name]
                        ON [dbo].[DocumentSystemOrganizationalUnits]([Name]) WHERE [IsActive] = 1;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentSystemProjects_IsActive_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[DocumentSystemProjects]'))
                    CREATE INDEX [IX_DocumentSystemProjects_IsActive_CreatedAtUtc]
                        ON [dbo].[DocumentSystemProjects]([IsActive], [CreatedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentSystemProjects_Name' AND object_id = OBJECT_ID(N'[dbo].[DocumentSystemProjects]'))
                    CREATE UNIQUE INDEX [IX_DocumentSystemProjects_Name]
                        ON [dbo].[DocumentSystemProjects]([Name]) WHERE [IsActive] = 1;
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
                   AND OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]', N'U') IS NOT NULL
                   AND COL_LENGTH('dbo.Documents', 'OrganizationalUnitId') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemOrganizationalUnits_OrganizationalUnitId')
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemOrganizationalUnits')
                    ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentSystemOrganizationalUnits_OrganizationalUnitId]
                        FOREIGN KEY ([OrganizationalUnitId]) REFERENCES [dbo].[DocumentSystemOrganizationalUnits]([Id]) ON DELETE SET NULL;

                IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
                   AND OBJECT_ID(N'[dbo].[DocumentSystemProjects]', N'U') IS NOT NULL
                   AND COL_LENGTH('dbo.Documents', 'ProjectId') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemProjects_ProjectId')
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemProjects')
                    ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentSystemProjects_ProjectId]
                        FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[DocumentSystemProjects]([Id]) ON DELETE SET NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReferralSmsEnabled') IS NOT NULL
                BEGIN
                    UPDATE [dbo].[SmsSettings]
                    SET
                        [DocumentApprovalReferralSmsEnabled] = 1,
                        [DocumentApprovalReminderDelayDays] = 0,
                        [DocumentApprovalReminderDelayHours] = 24,
                        [DocumentApprovalReminderSmsEnabled] = 0,
                        [DocumentOwnerStepApprovalNotifySmsEnabled] = 1,
                        [DocumentPostApprovalAssigneeSmsEnabled] = 1,
                        [DocumentWorkflowCompletedOwnerSmsEnabled] = 1,
                        [DocumentWorkflowRejectedOwnerSmsEnabled] = 1
                    WHERE [Id] = 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemOrganizationalUnits_OrganizationalUnitId')
                    ALTER TABLE [dbo].[Documents] DROP CONSTRAINT [FK_Documents_DocumentSystemOrganizationalUnits_OrganizationalUnitId];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemOrganizationalUnits')
                    ALTER TABLE [dbo].[Documents] DROP CONSTRAINT [FK_Documents_DocumentSystemOrganizationalUnits];

                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemProjects_ProjectId')
                    ALTER TABLE [dbo].[Documents] DROP CONSTRAINT [FK_Documents_DocumentSystemProjects_ProjectId];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemProjects')
                    ALTER TABLE [dbo].[Documents] DROP CONSTRAINT [FK_Documents_DocumentSystemProjects];
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[DocumentSystemOrganizationalUnits];
                IF OBJECT_ID(N'[dbo].[DocumentSystemProjects]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[DocumentSystemProjects];
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_OrganizationalUnitId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                    DROP INDEX [IX_Documents_OrganizationalUnitId] ON [dbo].[Documents];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ProjectId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                    DROP INDEX [IX_Documents_ProjectId] ON [dbo].[Documents];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Documents', 'OrganizationalUnitId') IS NOT NULL
                    ALTER TABLE [dbo].[Documents] DROP COLUMN [OrganizationalUnitId];
                IF COL_LENGTH('dbo.Documents', 'ProjectId') IS NOT NULL
                    ALTER TABLE [dbo].[Documents] DROP COLUMN [ProjectId];
                IF COL_LENGTH('dbo.DocumentFolders', 'Description') IS NOT NULL
                    ALTER TABLE [dbo].[DocumentFolders] DROP COLUMN [Description];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReferralSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentApprovalReferralSmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReferralSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentApprovalReferralSmsEnabled];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayDays') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayDays_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayDays') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentApprovalReminderDelayDays];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayHours') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayHours_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayHours') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentApprovalReminderDelayHours];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderSmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentApprovalReminderSmsEnabled];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentOwnerStepApprovalNotifySmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentOwnerStepApprovalNotifySmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentOwnerStepApprovalNotifySmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentOwnerStepApprovalNotifySmsEnabled];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentPostApprovalAssigneeSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentPostApprovalAssigneeSmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentPostApprovalAssigneeSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentPostApprovalAssigneeSmsEnabled];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowCompletedOwnerSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentWorkflowCompletedOwnerSmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowCompletedOwnerSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentWorkflowCompletedOwnerSmsEnabled];

                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowRejectedOwnerSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [DF_SmsSettings_DocumentWorkflowRejectedOwnerSmsEnabled_Mig];
                IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowRejectedOwnerSmsEnabled') IS NOT NULL
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [DocumentWorkflowRejectedOwnerSmsEnabled];
                """);
        }
    }
}
