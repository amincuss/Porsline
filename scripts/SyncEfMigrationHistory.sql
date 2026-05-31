-- Run once on an EXISTING database when EF migration history was reset but tables already exist.
-- After this, use: Update-Database  (or dotnet ef database update)

IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531074423_BaselineRestore')
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260531074423_BaselineRestore', N'10.0.0');
GO

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531074511_vasssjjن')
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260531074511_vasssjjن', N'10.0.0');
GO

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531074551_vasssjjنکک')
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260531074551_vasssjjنکک', N'10.0.0');
GO

-- Run Update-Database after syncing history above, OR apply this migration SQL manually:
-- MigrationId: 20260531074753_AddFormWordTemplateStampColumns

IF COL_LENGTH('dbo.FormWordTemplates', 'StampPlaceholderKey') IS NULL
    ALTER TABLE [dbo].[FormWordTemplates] ADD [StampPlaceholderKey] nvarchar(120) NULL;
IF COL_LENGTH('dbo.FormWordTemplates', 'StampImagePath') IS NULL
    ALTER TABLE [dbo].[FormWordTemplates] ADD [StampImagePath] nvarchar(500) NULL;
IF COL_LENGTH('dbo.FormFields', 'DefaultValue') IS NULL
    ALTER TABLE [dbo].[FormFields] ADD [DefaultValue] nvarchar(2000) NULL;
IF COL_LENGTH('dbo.FormFields', 'IsReadOnly') IS NULL
    ALTER TABLE [dbo].[FormFields] ADD [IsReadOnly] bit NOT NULL CONSTRAINT [DF_FormFields_IsReadOnly] DEFAULT (0);
GO

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531074753_AddFormWordTemplateStampColumns')
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260531074753_AddFormWordTemplateStampColumns', N'10.0.0');
GO
