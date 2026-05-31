-- Idempotent: creates Hangfire batch Word export job table.
IF OBJECT_ID(N'[dbo].[FormWordBatchExportJobs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FormWordBatchExportJobs] (
        [Id] uniqueidentifier NOT NULL,
        [TemplateId] uniqueidentifier NOT NULL,
        [SubmissionIdsJson] nvarchar(max) NOT NULL,
        [ImageOverridesJson] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [TotalCount] int NOT NULL,
        [ProcessedCount] int NOT NULL,
        [ZipFilePath] nvarchar(500) NULL,
        [ZipFileName] nvarchar(260) NULL,
        [ErrorMessage] nvarchar(2000) NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [HangfireJobId] nvarchar(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_FormWordBatchExportJobs] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_FormWordBatchExportJobs_CreatedAtUtc] ON [dbo].[FormWordBatchExportJobs]([CreatedAtUtc]);
    CREATE INDEX [IX_FormWordBatchExportJobs_CreatedByUserId] ON [dbo].[FormWordBatchExportJobs]([CreatedByUserId]);
    CREATE INDEX [IX_FormWordBatchExportJobs_Status] ON [dbo].[FormWordBatchExportJobs]([Status]);
END

IF OBJECT_ID(N'[dbo].[FormWordBatchExportJobs]', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20260531120000_AddFormWordBatchExportJobs')
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260531120000_AddFormWordBatchExportJobs', N'10.0.0');
