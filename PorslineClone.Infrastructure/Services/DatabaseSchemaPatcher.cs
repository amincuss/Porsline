using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>پچ ایمن schema برای سرورهایی که قبلاً دیتابیس دارند (بدون CreateTable کل پروژه)</summary>
public static class DatabaseSchemaPatcher
{
    public static async Task ApplyAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        logger.LogInformation("Applying database schema patch (UserPositions, signatures, contract columns)...");

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[UserPositions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[UserPositions] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(150) NOT NULL,
                    [SortOrder] int NOT NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_UserPositions] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_UserPositions_Name] ON [dbo].[UserPositions]([Name]);
                CREATE INDEX [IX_UserPositions_IsActive_SortOrder] ON [dbo].[UserPositions]([IsActive], [SortOrder]);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.AspNetUsers', 'UserPositionId') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [UserPositionId] uniqueidentifier NULL;

            IF COL_LENGTH('dbo.AspNetUsers', 'SignatureImagePath') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [SignatureImagePath] nvarchar(500) NULL;
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_AspNetUsers_UserPositions_UserPositionId'
            )
            AND COL_LENGTH('dbo.AspNetUsers', 'UserPositionId') IS NOT NULL
            AND OBJECT_ID(N'[dbo].[UserPositions]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[AspNetUsers] WITH CHECK ADD CONSTRAINT [FK_AspNetUsers_UserPositions_UserPositionId]
                    FOREIGN KEY([UserPositionId]) REFERENCES [dbo].[UserPositions]([Id]) ON DELETE SET NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.Contracts', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.Contracts', 'OriginalFilePath') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [OriginalFilePath] nvarchar(500) NULL;
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[ContractApprovalLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractApprovalLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [ContractId] uniqueidentifier NOT NULL,
                    [AssigneeUserId] uniqueidentifier NOT NULL,
                    [Code] nvarchar(32) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ExpiresAtUtc] datetime2 NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_ContractApprovalLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ContractApprovalLinks_Contracts_ContractId]
                        FOREIGN KEY ([ContractId]) REFERENCES [dbo].[Contracts]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_ContractApprovalLinks_Code] ON [dbo].[ContractApprovalLinks]([Code]);
                CREATE INDEX [IX_ContractApprovalLinks_ContractId_AssigneeUserId_IsActive]
                    ON [dbo].[ContractApprovalLinks]([ContractId], [AssigneeUserId], [IsActive]);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE [dbo].[Contracts]
            SET [OriginalFilePath] = [FilePath]
            WHERE [OriginalFilePath] IS NULL
              AND [FilePath] IS NOT NULL
              AND [FilePath] NOT LIKE '%_signed_%';
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.SmsSettings', 'ContractCreatorApprovalNotifySmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractCreatorApprovalNotifySmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractCreatorApprovalNotifySmsEnabled] DEFAULT (1);
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[ContractDocumentTemplates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractDocumentTemplates] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [Description] nvarchar(1000) NULL,
                    [IsActive] bit NOT NULL,
                    [ActiveVersionId] uniqueidentifier NULL,
                    [CreatedByUserId] uniqueidentifier NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NULL,
                    CONSTRAINT [PK_ContractDocumentTemplates] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplates_Name] ON [dbo].[ContractDocumentTemplates]([Name]);
                CREATE INDEX [IX_ContractDocumentTemplates_IsActive_CreatedAtUtc] ON [dbo].[ContractDocumentTemplates]([IsActive], [CreatedAtUtc]);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[ContractDocumentTemplateVersions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractDocumentTemplateVersions] (
                    [Id] uniqueidentifier NOT NULL,
                    [TemplateId] uniqueidentifier NOT NULL,
                    [VersionNumber] int NOT NULL,
                    [FilePath] nvarchar(500) NOT NULL,
                    [FileName] nvarchar(260) NOT NULL,
                    [DetectedPlaceholdersJson] nvarchar(8000) NOT NULL,
                    [ChangeNote] nvarchar(500) NULL,
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_ContractDocumentTemplateVersions] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ContractDocumentTemplateVersions_ContractDocumentTemplates_TemplateId]
                        FOREIGN KEY ([TemplateId]) REFERENCES [dbo].[ContractDocumentTemplates]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplateVersions_TemplateId_VersionNumber]
                    ON [dbo].[ContractDocumentTemplateVersions]([TemplateId], [VersionNumber]);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractDocumentTemplateFields] (
                    [Id] uniqueidentifier NOT NULL,
                    [TemplateId] uniqueidentifier NOT NULL,
                    [Key] nvarchar(80) NOT NULL,
                    [Label] nvarchar(200) NOT NULL,
                    [FieldType] int NOT NULL,
                    [IsRequired] bit NOT NULL,
                    [SortOrder] int NOT NULL,
                    [DesignerOrderJson] nvarchar(500) NULL,
                    [DefaultValue] nvarchar(500) NULL,
                    [OptionsJson] nvarchar(2000) NULL,
                    CONSTRAINT [PK_ContractDocumentTemplateFields] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId]
                        FOREIGN KEY ([TemplateId]) REFERENCES [dbo].[ContractDocumentTemplates]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplateFields_TemplateId_Key]
                    ON [dbo].[ContractDocumentTemplateFields]([TemplateId], [Key]);
                CREATE INDEX [IX_ContractDocumentTemplateFields_TemplateId_SortOrder]
                    ON [dbo].[ContractDocumentTemplateFields]([TemplateId], [SortOrder]);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId'
            )
                ALTER TABLE [dbo].[ContractDocumentTemplates] DROP CONSTRAINT [FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId];
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId'
            )
            AND OBJECT_ID(N'[dbo].[ContractDocumentTemplateVersions]', N'U') IS NOT NULL
            AND OBJECT_ID(N'[dbo].[ContractDocumentTemplates]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplates] WITH CHECK ADD CONSTRAINT [FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId]
                    FOREIGN KEY([ActiveVersionId]) REFERENCES [dbo].[ContractDocumentTemplateVersions]([Id]) ON DELETE NO ACTION;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.Contracts', 'ContractDocumentTemplateId') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [ContractDocumentTemplateId] uniqueidentifier NULL;

            IF COL_LENGTH('dbo.Contracts', 'ContractDocumentTemplateVersionId') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [ContractDocumentTemplateVersionId] uniqueidentifier NULL;

            IF COL_LENGTH('dbo.Contracts', 'TemplateFieldValuesJson') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [TemplateFieldValuesJson] nvarchar(max) NULL;

            IF COL_LENGTH('dbo.Contracts', 'PdfFilePath') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [PdfFilePath] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.ContractVersions', 'PdfFilePath') IS NULL
                ALTER TABLE [dbo].[ContractVersions] ADD [PdfFilePath] nvarchar(500) NULL;
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_Contracts_ContractDocumentTemplates_ContractDocumentTemplateId'
            )
            AND COL_LENGTH('dbo.Contracts', 'ContractDocumentTemplateId') IS NOT NULL
            AND OBJECT_ID(N'[dbo].[ContractDocumentTemplates]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[Contracts] WITH CHECK ADD CONSTRAINT [FK_Contracts_ContractDocumentTemplates_ContractDocumentTemplateId]
                    FOREIGN KEY([ContractDocumentTemplateId]) REFERENCES [dbo].[ContractDocumentTemplates]([Id]) ON DELETE SET NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.ContractDocumentTemplateFields', 'VersionId') IS NULL
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] ADD [VersionId] uniqueidentifier NULL;
            END
            ELSE IF EXISTS (
                SELECT 1 FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                WHERE t.name = N'ContractDocumentTemplateFields'
                  AND c.name = N'VersionId'
                  AND c.is_nullable = 0)
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE f
            SET f.[VersionId] = COALESCE(
                t.[ActiveVersionId],
                (SELECT TOP 1 v.[Id] FROM [dbo].[ContractDocumentTemplateVersions] v
                 WHERE v.[TemplateId] = f.[TemplateId]
                 ORDER BY v.[VersionNumber] DESC))
            FROM [dbo].[ContractDocumentTemplateFields] f
            INNER JOIN [dbo].[ContractDocumentTemplates] t ON t.[Id] = f.[TemplateId]
            WHERE f.[VersionId] IS NULL
               OR f.[VersionId] = '00000000-0000-0000-0000-000000000000';
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM [dbo].[ContractDocumentTemplateFields]
            WHERE [VersionId] IS NULL
               OR [VersionId] = '00000000-0000-0000-0000-000000000000';

            IF COL_LENGTH('dbo.ContractDocumentTemplateFields', 'VersionId') IS NOT NULL
               AND (SELECT is_nullable FROM sys.columns c
                    JOIN sys.tables t ON c.object_id = t.object_id
                    WHERE t.name = N'ContractDocumentTemplateFields' AND c.name = N'VersionId') = 1
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NOT NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_TemplateId_Key'
                       AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                DROP INDEX [IX_ContractDocumentTemplateFields_TemplateId_Key] ON [dbo].[ContractDocumentTemplateFields];

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_Key'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplateFields_VersionId_Key]
                    ON [dbo].[ContractDocumentTemplateFields]([VersionId], [Key]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_SortOrder'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                CREATE INDEX [IX_ContractDocumentTemplateFields_VersionId_SortOrder]
                    ON [dbo].[ContractDocumentTemplateFields]([VersionId], [SortOrder]);

            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_ContractDocumentTemplateFields_ContractDocumentTemplateVersions_VersionId')
            AND OBJECT_ID(N'[dbo].[ContractDocumentTemplateVersions]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] WITH CHECK ADD
                    CONSTRAINT [FK_ContractDocumentTemplateFields_ContractDocumentTemplateVersions_VersionId]
                    FOREIGN KEY([VersionId]) REFERENCES [dbo].[ContractDocumentTemplateVersions]([Id]) ON DELETE CASCADE;
            END
            """, ct);

        logger.LogInformation("Database schema patch completed.");
    }
}
