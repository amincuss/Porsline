using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>پچ ایمن schema برای سرورهایی که قبلاً دیتابیس دارند (بدون CreateTable کل پروژه)</summary>
public static class DatabaseSchemaPatcher
{
    /// <summary>اجرای SQL بدون پارس‌کردن نام ستون‌ها توسط EF (جلوگیری از خطای پارامتر ۸۰۰۰).</summary>
    private static async Task ExecuteScriptAsync(AppDbContext db, string sql, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 180;
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task ApplySecuritySettingsColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.SecuritySettings', 'AnonymousLinkExpiryDays') IS NULL
                ALTER TABLE [dbo].[SecuritySettings] ADD [AnonymousLinkExpiryDays] int NOT NULL
                    CONSTRAINT [DF_SecuritySettings_AnonymousLinkExpiryDays] DEFAULT (7);

            IF COL_LENGTH('dbo.SecuritySettings', 'DispatchLinkRequireOtp') IS NULL
                ALTER TABLE [dbo].[SecuritySettings] ADD [DispatchLinkRequireOtp] bit NOT NULL
                    CONSTRAINT [DF_SecuritySettings_DispatchLinkRequireOtp] DEFAULT (0);

            IF COL_LENGTH('dbo.SecuritySettings', 'AccessTokenLifetimeMinutes') IS NULL
                ALTER TABLE [dbo].[SecuritySettings] ADD [AccessTokenLifetimeMinutes] int NOT NULL
                    CONSTRAINT [DF_SecuritySettings_AccessTokenLifetimeMinutes] DEFAULT (180);

            IF COL_LENGTH('dbo.SecuritySettings', 'RefreshTokenLifetimeDays') IS NULL
                ALTER TABLE [dbo].[SecuritySettings] ADD [RefreshTokenLifetimeDays] int NOT NULL
                    CONSTRAINT [DF_SecuritySettings_RefreshTokenLifetimeDays] DEFAULT (7);
            """, ct);
    }

    /// <summary>ستون‌های گردش سند — batch جدا برای جلوگیری از خطای compile SQL Server.</summary>
    public static async Task EnsureDocumentWorkflowSchemaAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF COL_LENGTH('dbo.Documents', 'WorkflowStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStatus] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowStatus] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowName') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowName] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.Documents', 'StepsJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [StepsJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'CurrentStepOrder') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [CurrentStepOrder] int NOT NULL
                    CONSTRAINT [DF_Documents_CurrentStepOrder] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'PostApprovalJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [PostApprovalJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunCycle') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunCycle] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowRunCycle] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunsHistoryJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunsHistoryJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRejectionJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRejectionJson] nvarchar(max) NULL;
            """, ct);

        logger?.LogInformation("Document workflow schema columns ensured.");
    }

    /// <summary>ستون‌های envelope encryption برای DocumentVersions.</summary>
    public static async Task EnsureDocumentEncryptionSchemaAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[DocumentVersions]', N'U') IS NULL RETURN;
            IF COL_LENGTH('dbo.DocumentVersions', 'IsEncrypted') IS NULL
                ALTER TABLE [dbo].[DocumentVersions] ADD [IsEncrypted] bit NOT NULL CONSTRAINT [DF_DocumentVersions_IsEncrypted] DEFAULT(0);
            IF COL_LENGTH('dbo.DocumentVersions', 'EncryptionKeyId') IS NULL
                ALTER TABLE [dbo].[DocumentVersions] ADD [EncryptionKeyId] nvarchar(32) NULL;
            IF COL_LENGTH('dbo.DocumentVersions', 'FileNonceBase64') IS NULL
                ALTER TABLE [dbo].[DocumentVersions] ADD [FileNonceBase64] nvarchar(32) NULL;
            IF COL_LENGTH('dbo.DocumentVersions', 'EncryptedDekBase64') IS NULL
                ALTER TABLE [dbo].[DocumentVersions] ADD [EncryptedDekBase64] nvarchar(256) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentVersions_IsEncrypted_EncryptionKeyId' AND object_id = OBJECT_ID(N'dbo.DocumentVersions'))
                CREATE INDEX [IX_DocumentVersions_IsEncrypted_EncryptionKeyId]
                    ON [dbo].[DocumentVersions]([IsEncrypted], [EncryptionKeyId]);
            """, ct);

        logger?.LogInformation("Document encryption schema columns ensured.");
    }

    /// <summary>طبقه‌بندی: واحد سازمانی، پروژه، توضیح پوشه.</summary>
    public static async Task EnsureDocumentClassificationSchemaAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentSystemOrganizationalUnits] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemOrganizationalUnits_IsActive] DEFAULT (1),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentSystemOrganizationalUnits] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentSystemOrganizationalUnits_Name]
                    ON [dbo].[DocumentSystemOrganizationalUnits]([Name]) WHERE [IsActive] = 1;
            END

            IF OBJECT_ID(N'[dbo].[DocumentSystemProjects]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentSystemProjects] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemProjects_IsActive] DEFAULT (1),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentSystemProjects] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentSystemProjects_Name]
                    ON [dbo].[DocumentSystemProjects]([Name]) WHERE [IsActive] = 1;
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

            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentSystemOrganizationalUnits]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemOrganizationalUnits')
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentSystemOrganizationalUnits]
                    FOREIGN KEY ([OrganizationalUnitId]) REFERENCES [dbo].[DocumentSystemOrganizationalUnits]([Id]) ON DELETE SET NULL;

            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentSystemProjects]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentSystemProjects')
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentSystemProjects]
                    FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[DocumentSystemProjects]([Id]) ON DELETE SET NULL;
            """, ct);

        logger?.LogInformation("Document classification schema ensured.");
    }

    /// <summary>ستون‌ها و جداول چرخه عمر سند — در batchهای جدا (SQL Server کل batch را قبل از اجرا compile می‌کند).</summary>
    public static async Task EnsureDocumentLifecycleSchemaAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF COL_LENGTH('dbo.Documents', 'ExpiresAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ExpiresAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'RetentionPolicyId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [RetentionPolicyId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'ArchiveTier') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchiveTier] int NOT NULL CONSTRAINT [DF_Documents_ArchiveTier] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LifecycleStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LifecycleStatus] int NOT NULL CONSTRAINT [DF_Documents_LifecycleStatus] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'IsArchived') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsArchived] bit NOT NULL CONSTRAINT [DF_Documents_IsArchived] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'ArchivedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchivedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHold') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHold] bit NOT NULL CONSTRAINT [DF_Documents_LegalHold] DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LegalHoldReason') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldReason] nvarchar(500) NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldByUserId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldByUserId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'IsObsolete') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsObsolete] bit NOT NULL CONSTRAINT [DF_Documents_IsObsolete] DEFAULT(0);
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
                ALTER TABLE [dbo].[Documents] ADD [LongTermRetention] bit NOT NULL CONSTRAINT [DF_Documents_LongTermRetention] DEFAULT(0);
            """, ct);

        await ExecuteScriptAsync(db,
            """
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
                    [ExpirationWarningDays] int NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_ExpirationWarningDays] DEFAULT(30),
                    [LongTermRetention] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_LongTermRetention] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_IsActive] DEFAULT(1),
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_IsDefault] DEFAULT(0),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_DocumentRetentionPolicies_SortOrder] DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentRetentionPolicies] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentRetentionPolicies_Name] ON [dbo].[DocumentRetentionPolicies]([Name]);
                CREATE INDEX [IX_DocumentRetentionPolicies_IsActive_IsDefault_SortOrder]
                    ON [dbo].[DocumentRetentionPolicies]([IsActive], [IsDefault], [SortOrder]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentLifecycleSettings] (
                    [Id] uniqueidentifier NOT NULL,
                    [DefaultRetentionPolicyId] uniqueidentifier NULL,
                    [AutoProcessEnabled] bit NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_AutoProcessEnabled] DEFAULT(1),
                    [DefaultExpirationWarningDays] int NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_DefaultExpirationWarningDays] DEFAULT(30),
                    [ProcessIntervalHours] int NOT NULL CONSTRAINT [DF_DocumentLifecycleSettings_ProcessIntervalHours] DEFAULT(6),
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentLifecycleSettings] PRIMARY KEY ([Id])
                );
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL RETURN;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_IsArchived] ON [dbo].[Documents]([IsArchived]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_LegalHold' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_LegalHold] ON [dbo].[Documents]([LegalHold]) WHERE [LegalHold] = 1;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_LifecycleStatus_IsArchived_UpdatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_LifecycleStatus_IsArchived_UpdatedAtUtc]
                    ON [dbo].[Documents]([LifecycleStatus], [IsArchived], [UpdatedAtUtc]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_RetentionPolicyId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_RetentionPolicyId] ON [dbo].[Documents]([RetentionPolicyId])
                    WHERE [RetentionPolicyId] IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ScheduledArchiveAtUtc_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_ScheduledArchiveAtUtc_IsArchived] ON [dbo].[Documents]([ScheduledArchiveAtUtc], [IsArchived])
                    WHERE [ScheduledArchiveAtUtc] IS NOT NULL AND [IsArchived] = 0;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ScheduledDeleteAtUtc_LegalHold_LongTermRetention' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_ScheduledDeleteAtUtc_LegalHold_LongTermRetention]
                    ON [dbo].[Documents]([ScheduledDeleteAtUtc], [LegalHold], [LongTermRetention])
                    WHERE [ScheduledDeleteAtUtc] IS NOT NULL AND [LegalHold] = 0 AND [LongTermRetention] = 0;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_WorkflowStatus_CurrentStepOrder' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_WorkflowStatus_CurrentStepOrder] ON [dbo].[Documents]([WorkflowStatus], [CurrentStepOrder]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_WorkflowTemplateId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_WorkflowTemplateId] ON [dbo].[Documents]([WorkflowTemplateId])
                    WHERE [WorkflowTemplateId] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentRetentionPolicies_RetentionPolicyId')
               AND COL_LENGTH('dbo.Documents', 'RetentionPolicyId') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentRetentionPolicies]', N'U') IS NOT NULL
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentRetentionPolicies_RetentionPolicyId]
                    FOREIGN KEY ([RetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentLifecycleSettings_DocumentRetentionPolicies_DefaultRetentionPolicyId')
               AND OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[DocumentRetentionPolicies]', N'U') IS NOT NULL
                ALTER TABLE [dbo].[DocumentLifecycleSettings] ADD CONSTRAINT [FK_DocumentLifecycleSettings_DocumentRetentionPolicies_DefaultRetentionPolicyId]
                    FOREIGN KEY ([DefaultRetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentLifecycleSettings_DefaultRetentionPolicyId' AND object_id = OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]'))
                CREATE INDEX [IX_DocumentLifecycleSettings_DefaultRetentionPolicyId] ON [dbo].[DocumentLifecycleSettings]([DefaultRetentionPolicyId]);
            """, ct);

        logger?.LogInformation("Document lifecycle schema ensured.");
    }

    /// <summary>جداول و ستون‌های پورتال عمومی اسناد.</summary>
    public static async Task EnsurePublicDocumentPortalSchemaAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[PublicCategories]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PublicCategories] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [Slug] nvarchar(120) NOT NULL,
                    [Description] nvarchar(1000) NULL,
                    [CoverImagePath] nvarchar(500) NULL,
                    [Icon] nvarchar(80) NULL,
                    [SortOrder] int NOT NULL CONSTRAINT [DF_PublicCategories_SortOrder] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_PublicCategories_IsActive] DEFAULT(1),
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PublicCategories_CreatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_PublicCategories] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_PublicCategories_Slug] ON [dbo].[PublicCategories]([Slug]);
                CREATE INDEX [IX_PublicCategories_IsActive_SortOrder] ON [dbo].[PublicCategories]([IsActive], [SortOrder]);
            END

            IF OBJECT_ID(N'[dbo].[PublicCollections]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PublicCollections] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [Slug] nvarchar(120) NOT NULL,
                    [Description] nvarchar(1000) NULL,
                    [CoverImagePath] nvarchar(500) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_PublicCollections_IsActive] DEFAULT(1),
                    [Featured] bit NOT NULL CONSTRAINT [DF_PublicCollections_Featured] DEFAULT(0),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_PublicCollections_SortOrder] DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PublicCollections_CreatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_PublicCollections] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_PublicCollections_Slug] ON [dbo].[PublicCollections]([Slug]);
                CREATE INDEX [IX_PublicCollections_IsActive_Featured_SortOrder] ON [dbo].[PublicCollections]([IsActive], [Featured], [SortOrder]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[PublicBanners]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PublicBanners] (
                    [Id] uniqueidentifier NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [Subtitle] nvarchar(400) NULL,
                    [ImagePath] nvarchar(500) NULL,
                    [CtaLabel] nvarchar(80) NULL,
                    [CtaUrl] nvarchar(500) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_PublicBanners_IsActive] DEFAULT(1),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_PublicBanners_SortOrder] DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PublicBanners_CreatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_PublicBanners] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_PublicBanners_IsActive_SortOrder] ON [dbo].[PublicBanners]([IsActive], [SortOrder]);
            END

            IF OBJECT_ID(N'[dbo].[PublicPortalSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PublicPortalSettings] (
                    [Id] uniqueidentifier NOT NULL,
                    [PortalEnabled] bit NOT NULL CONSTRAINT [DF_PublicPortalSettings_PortalEnabled] DEFAULT(1),
                    [SiteTitle] nvarchar(200) NOT NULL,
                    [LogoPath] nvarchar(500) NULL,
                    [PrimaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_PublicPortalSettings_PrimaryColor] DEFAULT('#4f46e5'),
                    [SecondaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_PublicPortalSettings_SecondaryColor] DEFAULT('#0f172a'),
                    [AboutText] nvarchar(2000) NULL,
                    [ContactEmail] nvarchar(200) NULL,
                    [ContactPhone] nvarchar(40) NULL,
                    [FooterLinksJson] nvarchar(max) NULL,
                    [SocialLinksJson] nvarchar(max) NULL,
                    [ShowViewCounts] bit NOT NULL CONSTRAINT [DF_PublicPortalSettings_ShowViewCounts] DEFAULT(1),
                    [ShowDownloadCounts] bit NOT NULL CONSTRAINT [DF_PublicPortalSettings_ShowDownloadCounts] DEFAULT(1),
                    [AllowDownloads] bit NOT NULL CONSTRAINT [DF_PublicPortalSettings_AllowDownloads] DEFAULT(1),
                    [EnablePreviews] bit NOT NULL CONSTRAINT [DF_PublicPortalSettings_EnablePreviews] DEFAULT(1),
                    [FeaturedSectionSize] int NOT NULL CONSTRAINT [DF_PublicPortalSettings_FeaturedSectionSize] DEFAULT(6),
                    [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PublicPortalSettings_UpdatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_PublicPortalSettings] PRIMARY KEY ([Id])
                );
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[DocumentPublicProfiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentPublicProfiles] (
                    [DocumentId] uniqueidentifier NOT NULL,
                    [Slug] nvarchar(200) NOT NULL,
                    [Summary] nvarchar(500) NULL,
                    [PublicDescription] nvarchar(4000) NULL,
                    [DocumentType] nvarchar(80) NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_DocumentType] DEFAULT('Document'),
                    [PublicCategoryId] uniqueidentifier NULL,
                    [PublicCollectionId] uniqueidentifier NULL,
                    [CoverImagePath] nvarchar(500) NULL,
                    [PreviewAvailable] bit NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_PreviewAvailable] DEFAULT(1),
                    [DownloadAllowed] bit NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_DownloadAllowed] DEFAULT(1),
                    [PublicVisibilityStatus] int NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_PublicVisibilityStatus] DEFAULT(0),
                    [PublishedAtUtc] datetime2 NULL,
                    [PublishStartAtUtc] datetime2 NULL,
                    [PublishEndAtUtc] datetime2 NULL,
                    [PublicVersionId] uniqueidentifier NULL,
                    [Language] nvarchar(10) NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_Language] DEFAULT('fa'),
                    [Featured] bit NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_Featured] DEFAULT(0),
                    [Pinned] bit NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_Pinned] DEFAULT(0),
                    [SeoTitle] nvarchar(200) NULL,
                    [SeoDescription] nvarchar(500) NULL,
                    [SeoKeywords] nvarchar(300) NULL,
                    [PublicViewCount] bigint NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_PublicViewCount] DEFAULT(0),
                    [PublicDownloadCount] bigint NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_PublicDownloadCount] DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_CreatedAtUtc] DEFAULT(GETUTCDATE()),
                    [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_DocumentPublicProfiles_UpdatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_DocumentPublicProfiles] PRIMARY KEY ([DocumentId])
                );
                CREATE UNIQUE INDEX [IX_DocumentPublicProfiles_Slug] ON [dbo].[DocumentPublicProfiles]([Slug]);
                CREATE INDEX [IX_DocumentPublicProfiles_PublicVisibilityStatus_PublishedAtUtc] ON [dbo].[DocumentPublicProfiles]([PublicVisibilityStatus], [PublishedAtUtc]);
                CREATE INDEX [IX_DocumentPublicProfiles_PublicCategoryId_Featured_Pinned] ON [dbo].[DocumentPublicProfiles]([PublicCategoryId], [Featured], [Pinned]);
                CREATE INDEX [IX_DocumentPublicProfiles_PublicCollectionId_PublishedAtUtc] ON [dbo].[DocumentPublicProfiles]([PublicCollectionId], [PublishedAtUtc]);
                CREATE INDEX [IX_DocumentPublicProfiles_Language] ON [dbo].[DocumentPublicProfiles]([Language]);
                CREATE INDEX [IX_DocumentPublicProfiles_PublishStartAtUtc_PublishEndAtUtc] ON [dbo].[DocumentPublicProfiles]([PublishStartAtUtc], [PublishEndAtUtc]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[PublicAnalyticsEvents]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PublicAnalyticsEvents] (
                    [Id] uniqueidentifier NOT NULL,
                    [EventType] int NOT NULL,
                    [DocumentId] uniqueidentifier NULL,
                    [PublicCategoryId] uniqueidentifier NULL,
                    [PublicCollectionId] uniqueidentifier NULL,
                    [VisitorKey] nvarchar(64) NULL,
                    [IpHash] nvarchar(64) NULL,
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PublicAnalyticsEvents_CreatedAtUtc] DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_PublicAnalyticsEvents] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_PublicAnalyticsEvents_DocumentId_EventType_CreatedAtUtc] ON [dbo].[PublicAnalyticsEvents]([DocumentId], [EventType], [CreatedAtUtc]);
                CREATE INDEX [IX_PublicAnalyticsEvents_VisitorKey_DocumentId_EventType_CreatedAtUtc] ON [dbo].[PublicAnalyticsEvents]([VisitorKey], [DocumentId], [EventType], [CreatedAtUtc]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[DocumentPublicProfiles]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentPublicProfiles_Documents_DocumentId')
                    ALTER TABLE [dbo].[DocumentPublicProfiles] DROP CONSTRAINT [FK_DocumentPublicProfiles_Documents_DocumentId];
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentPublicProfiles_Documents_DocumentId')
                    ALTER TABLE [dbo].[DocumentPublicProfiles] ADD CONSTRAINT [FK_DocumentPublicProfiles_Documents_DocumentId]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE NO ACTION;
            END

            IF OBJECT_ID(N'[dbo].[DocumentPublicProfiles]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[PublicCategories]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentPublicProfiles_PublicCategories_PublicCategoryId')
                ALTER TABLE [dbo].[DocumentPublicProfiles] ADD CONSTRAINT [FK_DocumentPublicProfiles_PublicCategories_PublicCategoryId]
                    FOREIGN KEY ([PublicCategoryId]) REFERENCES [dbo].[PublicCategories]([Id]) ON DELETE SET NULL;

            IF OBJECT_ID(N'[dbo].[DocumentPublicProfiles]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[PublicCollections]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentPublicProfiles_PublicCollections_PublicCollectionId')
                ALTER TABLE [dbo].[DocumentPublicProfiles] ADD CONSTRAINT [FK_DocumentPublicProfiles_PublicCollections_PublicCollectionId]
                    FOREIGN KEY ([PublicCollectionId]) REFERENCES [dbo].[PublicCollections]([Id]) ON DELETE SET NULL;

            IF OBJECT_ID(N'[dbo].[DocumentPublicProfiles]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[DocumentVersions]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DocumentPublicProfiles_DocumentVersions_PublicVersionId')
                ALTER TABLE [dbo].[DocumentPublicProfiles] ADD CONSTRAINT [FK_DocumentPublicProfiles_DocumentVersions_PublicVersionId]
                    FOREIGN KEY ([PublicVersionId]) REFERENCES [dbo].[DocumentVersions]([Id]) ON DELETE SET NULL;
            """, ct);

        logger?.LogInformation("Public document portal schema ensured.");
    }

    public static async Task ApplyAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        logger.LogInformation("Applying database schema patch (security settings, UserPositions, signatures, contract columns)...");

        await ApplySecuritySettingsColumnsAsync(db, ct);
        logger.LogInformation("SecuritySettings columns ensured.");

        await EnsureDocumentWorkflowSchemaAsync(db, logger, ct);
        await EnsureDocumentLifecycleSchemaAsync(db, logger, ct);
        await EnsureDocumentEncryptionSchemaAsync(db, logger, ct);
        await EnsureDocumentClassificationSchemaAsync(db, logger, ct);
        await EnsurePublicDocumentPortalSchemaAsync(db, logger, ct);

        await ExecuteScriptAsync(db,
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
                CREATE UNIQUE INDEX [IX_UserPositions_Name] ON [dbo].[UserPositions]([Name]) WHERE [IsDeleted] = 0;
                CREATE INDEX [IX_UserPositions_IsActive_SortOrder] ON [dbo].[UserPositions]([IsActive], [SortOrder]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.UserPositions', 'IsDeleted') IS NULL
                ALTER TABLE [dbo].[UserPositions] ADD [IsDeleted] bit NOT NULL
                    CONSTRAINT [DF_UserPositions_IsDeleted] DEFAULT (0);

            IF COL_LENGTH('dbo.UserPositions', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[UserPositions] ADD [DeletedAtUtc] datetime2 NULL;

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_UserPositions_Name' AND object_id = OBJECT_ID(N'[dbo].[UserPositions]')
                  AND has_filter = 0
            )
                DROP INDEX [IX_UserPositions_Name] ON [dbo].[UserPositions];

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_UserPositions_Name' AND object_id = OBJECT_ID(N'[dbo].[UserPositions]')
            )
                CREATE UNIQUE INDEX [IX_UserPositions_Name] ON [dbo].[UserPositions]([Name]) WHERE [IsDeleted] = 0;
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.AspNetUsers', 'UserPositionId') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [UserPositionId] uniqueidentifier NULL;

            IF COL_LENGTH('dbo.AspNetUsers', 'SignatureImagePath') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [SignatureImagePath] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.AspNetUsers', 'SignatureDisplayDegree') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [SignatureDisplayDegree] int NOT NULL
                    CONSTRAINT [DF_AspNetUsers_SignatureDisplayDegree] DEFAULT (60);

            IF COL_LENGTH('dbo.AspNetUsers', 'PersonnelCode') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [PersonnelCode] nvarchar(30) NULL;

            IF COL_LENGTH('dbo.AspNetUsers', 'Gender') IS NULL
                ALTER TABLE [dbo].[AspNetUsers] ADD [Gender] int NULL;
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.Contracts', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.Contracts', 'OriginalFilePath') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [OriginalFilePath] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.Contracts', 'IsSoftDeleted') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [IsSoftDeleted] bit NOT NULL
                    CONSTRAINT [DF_Contracts_IsSoftDeleted] DEFAULT (0);

            IF COL_LENGTH('dbo.Contracts', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [DeletedAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.Contracts', 'DeletedByUserId') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [DeletedByUserId] uniqueidentifier NULL;
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
            """
            UPDATE [dbo].[Contracts]
            SET [OriginalFilePath] = [FilePath]
            WHERE [OriginalFilePath] IS NULL
              AND [FilePath] IS NOT NULL
              AND [FilePath] NOT LIKE '%_signed_%';
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.SmsSettings', 'ContractCreatorApprovalNotifySmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractCreatorApprovalNotifySmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractCreatorApprovalNotifySmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'ContractAmendmentAssigneeSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractAmendmentAssigneeSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractAmendmentAssigneeSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'ContractAmendmentReturnToRejecterSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractAmendmentReturnToRejecterSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractAmendmentReturnToRejecterSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'ContractRejectionNotifySmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractRejectionNotifySmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractRejectionNotifySmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'ContractActionCompletedCreatorSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ContractActionCompletedCreatorSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ContractActionCompletedCreatorSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormActionPhaseCompletedSenderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormActionPhaseCompletedSenderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormActionPhaseCompletedSenderSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormResponderApprovedSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormResponderApprovedSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormResponderApprovedSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormSubmissionTrackingSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormSubmissionTrackingSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormSubmissionTrackingSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormWorkflowStartedResponderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormWorkflowStartedResponderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormWorkflowStartedResponderSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormWorkflowRejectedSenderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormWorkflowRejectedSenderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormWorkflowRejectedSenderSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.SmsSettings', 'FormWorkflowRejectedResponderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [FormWorkflowRejectedResponderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_FormWorkflowRejectedResponderSmsEnabled] DEFAULT (1);

            IF COL_LENGTH('dbo.Contracts', 'AmendmentJson') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [AmendmentJson] nvarchar(max) NULL;

            IF COL_LENGTH('dbo.Contracts', 'WorkflowEventsJson') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowEventsJson] nvarchar(max) NULL;

            IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_ApprovalReminderSmsEnabled] DEFAULT (0);

            IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderDelayDays') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderDelayDays] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_ApprovalReminderDelayDays] DEFAULT (0);

            IF COL_LENGTH('dbo.SmsSettings', 'ApprovalReminderDelayHours') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [ApprovalReminderDelayHours] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_ApprovalReminderDelayHours] DEFAULT (24);

            IF COL_LENGTH('dbo.ContractApprovalLinks', 'ReminderSmsSentAtUtc') IS NULL
                ALTER TABLE [dbo].[ContractApprovalLinks] ADD [ReminderSmsSentAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.ContractApprovalLinks', 'LinkOpenedAtUtc') IS NULL
                ALTER TABLE [dbo].[ContractApprovalLinks] ADD [LinkOpenedAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.FormSubmissionApprovalLinks', 'ReminderSmsSentAtUtc') IS NULL
                ALTER TABLE [dbo].[FormSubmissionApprovalLinks] ADD [ReminderSmsSentAtUtc] datetime2 NULL;
            """, ct);

        await ExecuteScriptAsync(db,
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
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplates_Name] ON [dbo].[ContractDocumentTemplates]([Name]) WHERE [IsDeleted] = 0;
                CREATE INDEX [IX_ContractDocumentTemplates_IsActive_CreatedAtUtc] ON [dbo].[ContractDocumentTemplates]([IsActive], [CreatedAtUtc]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.ContractDocumentTemplates', 'IsDeleted') IS NULL
                ALTER TABLE [dbo].[ContractDocumentTemplates] ADD [IsDeleted] bit NOT NULL
                    CONSTRAINT [DF_ContractDocumentTemplates_IsDeleted] DEFAULT (0);

            IF COL_LENGTH('dbo.ContractDocumentTemplates', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[ContractDocumentTemplates] ADD [DeletedAtUtc] datetime2 NULL;

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_ContractDocumentTemplates_Name'
                  AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplates]')
                  AND has_filter = 0
            )
                DROP INDEX [IX_ContractDocumentTemplates_Name] ON [dbo].[ContractDocumentTemplates];

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_ContractDocumentTemplates_Name'
                  AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplates]')
            )
                CREATE UNIQUE INDEX [IX_ContractDocumentTemplates_Name]
                    ON [dbo].[ContractDocumentTemplates]([Name]) WHERE [IsDeleted] = 0;

            IF COL_LENGTH('dbo.ContractDocumentTemplateVersions', 'IsDeleted') IS NULL
                ALTER TABLE [dbo].[ContractDocumentTemplateVersions] ADD [IsDeleted] bit NOT NULL
                    CONSTRAINT [DF_ContractDocumentTemplateVersions_IsDeleted] DEFAULT (0);

            IF COL_LENGTH('dbo.ContractDocumentTemplateVersions', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[ContractDocumentTemplateVersions] ADD [DeletedAtUtc] datetime2 NULL;
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[ContractDocumentTemplateVersions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractDocumentTemplateVersions] (
                    [Id] uniqueidentifier NOT NULL,
                    [TemplateId] uniqueidentifier NOT NULL,
                    [VersionNumber] int NOT NULL,
                    [FilePath] nvarchar(500) NOT NULL,
                    [FileName] nvarchar(260) NOT NULL,
                    [DetectedPlaceholdersJson] nvarchar(max) NOT NULL,
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

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.ContractDocumentTemplateVersions', 'DetectedPlaceholdersJson') IS NOT NULL
               AND (SELECT max_length FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.ContractDocumentTemplateVersions')
                      AND name = N'DetectedPlaceholdersJson') > 0
            BEGIN
                ALTER TABLE [dbo].[ContractDocumentTemplateVersions]
                    ALTER COLUMN [DetectedPlaceholdersJson] nvarchar(max) NOT NULL;
            END
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
            """
            IF EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId'
            )
                ALTER TABLE [dbo].[ContractDocumentTemplates] DROP CONSTRAINT [FK_ContractDocumentTemplates_ContractDocumentTemplateVersions_ActiveVersionId];
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
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

            IF COL_LENGTH('dbo.ContractVersions', 'IsAmendedVersion') IS NULL
                ALTER TABLE [dbo].[ContractVersions] ADD [IsAmendedVersion] bit NOT NULL
                    CONSTRAINT [DF_ContractVersions_IsAmendedVersion] DEFAULT (0);
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
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
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_Key'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                    DROP INDEX [IX_ContractDocumentTemplateFields_VersionId_Key] ON [dbo].[ContractDocumentTemplateFields];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_SortOrder'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                    DROP INDEX [IX_ContractDocumentTemplateFields_VersionId_SortOrder] ON [dbo].[ContractDocumentTemplateFields];
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NULL;
            END
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
            """
            DELETE FROM [dbo].[ContractDocumentTemplateFields]
            WHERE [VersionId] IS NULL
               OR [VersionId] = '00000000-0000-0000-0000-000000000000';

            IF COL_LENGTH('dbo.ContractDocumentTemplateFields', 'VersionId') IS NOT NULL
               AND (SELECT is_nullable FROM sys.columns c
                    JOIN sys.tables t ON c.object_id = t.object_id
                    WHERE t.name = N'ContractDocumentTemplateFields' AND c.name = N'VersionId') = 1
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_Key'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                    DROP INDEX [IX_ContractDocumentTemplateFields_VersionId_Key] ON [dbo].[ContractDocumentTemplateFields];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractDocumentTemplateFields_VersionId_SortOrder'
                           AND object_id = OBJECT_ID(N'[dbo].[ContractDocumentTemplateFields]'))
                    DROP INDEX [IX_ContractDocumentTemplateFields_VersionId_SortOrder] ON [dbo].[ContractDocumentTemplateFields];
                ALTER TABLE [dbo].[ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NOT NULL;
            END
            """, ct);

        await ExecuteScriptAsync(db,
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

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[FormWorkflowTemplates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormWorkflowTemplates] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [StepsJson] nvarchar(max) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedByUserId] uniqueidentifier NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_FormWorkflowTemplates] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_FormWorkflowTemplates_Name] ON [dbo].[FormWorkflowTemplates]([Name]);
                CREATE INDEX [IX_FormWorkflowTemplates_IsActive_CreatedAtUtc] ON [dbo].[FormWorkflowTemplates]([IsActive], [CreatedAtUtc]);
            END

            IF COL_LENGTH('dbo.Forms', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[Forms] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Forms', 'WorkflowName') IS NULL
                ALTER TABLE [dbo].[Forms] ADD [WorkflowName] nvarchar(200) NULL;

            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowName') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowName] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.FormSubmissions', 'ResponderId') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [ResponderId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.FormSubmissions', 'DispatchLinkId') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [DispatchLinkId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.FormSubmissions', 'IsArchived') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [IsArchived] bit NOT NULL CONSTRAINT DF_FormSubmissions_IsArchived DEFAULT(0);

            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowRunCycle') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowRunCycle] int NOT NULL
                    CONSTRAINT [DF_FormSubmissions_WorkflowRunCycle] DEFAULT (0);

            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowRunsHistoryJson') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowRunsHistoryJson] nvarchar(max) NULL;

            IF COL_LENGTH('dbo.FormSubmissions', 'WorkflowRejectionJson') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [WorkflowRejectionJson] nvarchar(max) NULL;

            IF COL_LENGTH('dbo.FormSubmissions', 'TrackingCode') IS NULL
                ALTER TABLE [dbo].[FormSubmissions] ADD [TrackingCode] nvarchar(32) NULL;

            IF OBJECT_ID(N'[dbo].[FormSubmissions]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.FormSubmissions', 'TrackingCode') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_FormSubmissions_TrackingCode'
                      AND object_id = OBJECT_ID(N'[dbo].[FormSubmissions]'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_FormSubmissions_TrackingCode]
                    ON [dbo].[FormSubmissions]([TrackingCode])
                    WHERE [TrackingCode] IS NOT NULL;

            IF OBJECT_ID(N'[dbo].[FormDispatchLinks]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.FormDispatchLinks', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[FormDispatchLinks] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.FormDispatchLinks', 'SentByUserId') IS NULL
                ALTER TABLE [dbo].[FormDispatchLinks] ADD [SentByUserId] uniqueidentifier NULL;

            IF COL_LENGTH('dbo.Responders', 'Gender') IS NULL
                ALTER TABLE [dbo].[Responders] ADD [Gender] int NULL;

            IF OBJECT_ID(N'[dbo].[FormSubmissionApprovalLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormSubmissionApprovalLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [FormSubmissionId] uniqueidentifier NOT NULL,
                    [AssigneeUserId] uniqueidentifier NOT NULL,
                    [Code] nvarchar(32) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ExpiresAtUtc] datetime2 NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_FormSubmissionApprovalLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_FormSubmissionApprovalLinks_FormSubmissions_FormSubmissionId]
                        FOREIGN KEY ([FormSubmissionId]) REFERENCES [dbo].[FormSubmissions]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_FormSubmissionApprovalLinks_Code] ON [dbo].[FormSubmissionApprovalLinks]([Code]);
                CREATE INDEX [IX_FormSubmissionApprovalLinks_FormSubmissionId_AssigneeUserId_IsActive]
                    ON [dbo].[FormSubmissionApprovalLinks]([FormSubmissionId], [AssigneeUserId], [IsActive]);
            END

            IF OBJECT_ID(N'[dbo].[FormActionLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormActionLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [FormSubmissionId] uniqueidentifier NOT NULL,
                    [AssigneeUserId] uniqueidentifier NOT NULL,
                    [Code] nvarchar(32) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ExpiresAtUtc] datetime2 NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_FormActionLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_FormActionLinks_FormSubmissions_FormSubmissionId]
                        FOREIGN KEY ([FormSubmissionId]) REFERENCES [dbo].[FormSubmissions]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_FormActionLinks_Code] ON [dbo].[FormActionLinks]([Code]);
                CREATE INDEX [IX_FormActionLinks_FormSubmissionId_AssigneeUserId_IsActive]
                    ON [dbo].[FormActionLinks]([FormSubmissionId], [AssigneeUserId], [IsActive]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[FormUserAccesses]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormUserAccesses] (
                    [FormId] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_FormUserAccesses_CreatedAtUtc] DEFAULT (GETUTCDATE()),
                    CONSTRAINT [PK_FormUserAccesses] PRIMARY KEY ([FormId], [UserId])
                );
                CREATE INDEX [IX_FormUserAccesses_UserId] ON [dbo].[FormUserAccesses]([UserId]);
            END

            IF OBJECT_ID(N'[dbo].[FormUserAccesses]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[Forms]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE name = N'FK_FormUserAccesses_Forms_FormId')
            BEGIN
                ALTER TABLE [dbo].[FormUserAccesses] WITH CHECK ADD CONSTRAINT [FK_FormUserAccesses_Forms_FormId]
                    FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms]([Id]) ON DELETE CASCADE;
            END

            IF OBJECT_ID(N'[dbo].[FormUserAccesses]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE name = N'FK_FormUserAccesses_AspNetUsers_UserId')
            BEGIN
                ALTER TABLE [dbo].[FormUserAccesses] WITH CHECK ADD CONSTRAINT [FK_FormUserAccesses_AspNetUsers_UserId]
                    FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE;
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.InboxMessages', 'IsArchived') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [IsArchived] bit NOT NULL CONSTRAINT DF_InboxMessages_IsArchived DEFAULT(0);
            IF COL_LENGTH('dbo.InboxMessages', 'ReadAtUtc') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [ReadAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.InboxMessages', 'SenderUserId') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [SenderUserId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.InboxMessages', 'IsHtml') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [IsHtml] bit NOT NULL CONSTRAINT DF_InboxMessages_IsHtml DEFAULT(0);
            IF COL_LENGTH('dbo.InboxMessages', 'AttachmentFileName') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [AttachmentFileName] nvarchar(260) NULL;
            IF COL_LENGTH('dbo.InboxMessages', 'AttachmentPath') IS NULL
                ALTER TABLE [dbo].[InboxMessages] ADD [AttachmentPath] nvarchar(500) NULL;
            IF EXISTS (
                SELECT 1 FROM sys.columns c
                WHERE c.object_id = OBJECT_ID(N'dbo.InboxMessages') AND c.name = N'Body'
                  AND c.max_length > 0 AND c.max_length <> -1)
                ALTER TABLE [dbo].[InboxMessages] ALTER COLUMN [Body] nvarchar(max) NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InboxMessages_SenderUserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.InboxMessages'))
                CREATE NONCLUSTERED INDEX [IX_InboxMessages_SenderUserId_CreatedAtUtc]
                    ON [dbo].[InboxMessages]([SenderUserId], [CreatedAtUtc]);
            """, ct);

        await ApplyRedundancyCleanupAndPerformanceIndexesAsync(db, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionKey') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionDirectionKey] nvarchar(80) NULL;
            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionLabel') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionDirectionLabel] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_ContractWorkflowTemplates_ActionAssignees] DEFAULT ('[]');

            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'CanvasLayoutJson') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [CanvasLayoutJson] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionKey') IS NULL
                ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionDirectionKey] nvarchar(80) NULL;
            IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionDirectionLabel') IS NULL
                ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionDirectionLabel] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.FormWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NULL
                ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL
                    CONSTRAINT [DF_FormWorkflowTemplates_ActionAssignees] DEFAULT ('[]');
            IF COL_LENGTH('dbo.FormWorkflowTemplates', 'CanvasLayoutJson') IS NULL
                ALTER TABLE [dbo].[FormWorkflowTemplates] ADD [CanvasLayoutJson] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'WorkflowValidityDays') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [WorkflowValidityDays] int NOT NULL
                    CONSTRAINT [DF_ContractWorkflowTemplates_WorkflowValidityDays] DEFAULT (0);
            IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'WorkflowValidityHours') IS NULL
                ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [WorkflowValidityHours] int NOT NULL
                    CONSTRAINT [DF_ContractWorkflowTemplates_WorkflowValidityHours] DEFAULT (0);

            IF COL_LENGTH('dbo.Contracts', 'WorkflowValidityEndsAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowValidityEndsAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Contracts', 'WorkflowValidityReminderSentAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowValidityReminderSentAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Contracts', 'SuspendedPendingUserId') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [SuspendedPendingUserId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Contracts', 'WorkflowIncompleteTerminatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowIncompleteTerminatedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Contracts', 'WorkflowIncompleteNote') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [WorkflowIncompleteNote] nvarchar(2000) NULL;

            IF COL_LENGTH('dbo.SmsSettings', 'WorkflowValidityReminderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [WorkflowValidityReminderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_WorkflowValidityReminderSmsEnabled] DEFAULT (0);
            IF COL_LENGTH('dbo.SmsSettings', 'WorkflowValiditySuspensionDelayDays') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [WorkflowValiditySuspensionDelayDays] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_WorkflowValiditySuspensionDelayDays] DEFAULT (0);
            IF COL_LENGTH('dbo.SmsSettings', 'WorkflowValiditySuspensionDelayHours') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [WorkflowValiditySuspensionDelayHours] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_WorkflowValiditySuspensionDelayHours] DEFAULT (24);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReferralSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReferralSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentApprovalReferralSmsEnabled] DEFAULT (1);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentOwnerStepApprovalNotifySmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentOwnerStepApprovalNotifySmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentOwnerStepApprovalNotifySmsEnabled] DEFAULT (1);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowCompletedOwnerSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentWorkflowCompletedOwnerSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentWorkflowCompletedOwnerSmsEnabled] DEFAULT (1);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentWorkflowRejectedOwnerSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentWorkflowRejectedOwnerSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentWorkflowRejectedOwnerSmsEnabled] DEFAULT (1);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentPostApprovalAssigneeSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentPostApprovalAssigneeSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentPostApprovalAssigneeSmsEnabled] DEFAULT (1);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderSmsEnabled') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderSmsEnabled] bit NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderSmsEnabled] DEFAULT (0);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayDays') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderDelayDays] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayDays] DEFAULT (0);
            IF COL_LENGTH('dbo.SmsSettings', 'DocumentApprovalReminderDelayHours') IS NULL
                ALTER TABLE [dbo].[SmsSettings] ADD [DocumentApprovalReminderDelayHours] int NOT NULL
                    CONSTRAINT [DF_SmsSettings_DocumentApprovalReminderDelayHours] DEFAULT (24);

            IF COL_LENGTH('dbo.Contracts', 'PostApprovalJson') IS NULL
                ALTER TABLE [dbo].[Contracts] ADD [PostApprovalJson] nvarchar(max) NULL;

            IF OBJECT_ID(N'[dbo].[ContractActionLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractActionLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [ContractId] uniqueidentifier NOT NULL,
                    [AssigneeUserId] uniqueidentifier NOT NULL,
                    [Code] nvarchar(32) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ExpiresAtUtc] datetime2 NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_ContractActionLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ContractActionLinks_Contracts_ContractId]
                        FOREIGN KEY ([ContractId]) REFERENCES [dbo].[Contracts]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_ContractActionLinks_Code] ON [dbo].[ContractActionLinks]([Code]);
                CREATE INDEX [IX_ContractActionLinks_ContractId_AssigneeUserId_IsActive]
                    ON [dbo].[ContractActionLinks]([ContractId], [AssigneeUserId], [IsActive]);
            END
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF COL_LENGTH('dbo.Responders', 'IsDeleted') IS NULL
                ALTER TABLE [dbo].[Responders] ADD [IsDeleted] bit NOT NULL
                    CONSTRAINT [DF_Responders_IsDeleted] DEFAULT (0);
            IF COL_LENGTH('dbo.Responders', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[Responders] ADD [DeletedAtUtc] datetime2 NULL;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Responders_MobileNumber' AND object_id = OBJECT_ID(N'dbo.Responders'))
                DROP INDEX [IX_Responders_MobileNumber] ON [dbo].[Responders];
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Responders_MobileNumber' AND object_id = OBJECT_ID(N'dbo.Responders'))
                CREATE UNIQUE INDEX [IX_Responders_MobileNumber] ON [dbo].[Responders]([MobileNumber]) WHERE [IsDeleted] = 0;

            IF COL_LENGTH('dbo.Responders', 'NationalCode') IS NULL
                ALTER TABLE [dbo].[Responders] ADD [NationalCode] nvarchar(50) NOT NULL
                    CONSTRAINT [DF_Responders_NationalCode] DEFAULT ('');
            IF COL_LENGTH('dbo.Responders', 'NationalCode') IS NOT NULL
               AND (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Responders') AND name = N'NationalCode') < 100
                ALTER TABLE [dbo].[Responders] ALTER COLUMN [NationalCode] nvarchar(50) NOT NULL;
            IF COL_LENGTH('dbo.AspNetUsers', 'NationalCode') IS NOT NULL
               AND (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AspNetUsers') AND name = N'NationalCode') < 100
                ALTER TABLE [dbo].[AspNetUsers] ALTER COLUMN [NationalCode] nvarchar(50) NOT NULL;
            IF COL_LENGTH('dbo.Contracts', 'NationalId') IS NOT NULL
               AND (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Contracts') AND name = N'NationalId') < 100
                ALTER TABLE [dbo].[Contracts] ALTER COLUMN [NationalId] nvarchar(50) NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Responders_NationalCode' AND object_id = OBJECT_ID(N'dbo.Responders'))
                DROP INDEX [IX_Responders_NationalCode] ON [dbo].[Responders];

            UPDATE [dbo].[Responders]
            SET [NationalCode] = LTRIM(RTRIM([NationalCode]))
            WHERE [NationalCode] IS NOT NULL;

            UPDATE [r]
            SET [NationalCode] = CONCAT(N'LEGACY-', LEFT(REPLACE(CAST([r].[Id] AS nvarchar(36)), N'-', N''), 32))
            FROM [dbo].[Responders] AS [r]
            WHERE [r].[IsDeleted] = 0
              AND ([r].[NationalCode] IS NULL OR [r].[NationalCode] = N'');

            ;WITH [DupNational] AS (
                SELECT
                    [Id],
                    [NationalCode],
                    ROW_NUMBER() OVER (
                        PARTITION BY [NationalCode]
                        ORDER BY [CreatedAtUtc], [Id]
                    ) AS [Rn]
                FROM [dbo].[Responders]
                WHERE [IsDeleted] = 0
                  AND LTRIM(RTRIM([NationalCode])) <> N''
            )
            UPDATE [r]
            SET [NationalCode] = LEFT(
                CONCAT([d].[NationalCode], N'-', LEFT(REPLACE(CAST([d].[Id] AS nvarchar(36)), N'-', N''), 8)),
                50
            )
            FROM [dbo].[Responders] AS [r]
            INNER JOIN [DupNational] AS [d] ON [r].[Id] = [d].[Id]
            WHERE [d].[Rn] > 1;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Responders_NationalCode' AND object_id = OBJECT_ID(N'dbo.Responders'))
                CREATE UNIQUE INDEX [IX_Responders_NationalCode] ON [dbo].[Responders]([NationalCode])
                    WHERE [IsDeleted] = 0 AND [NationalCode] <> N'';
            """, ct);

        await ExecuteScriptAsync(db,
            """
            IF OBJECT_ID(N'[dbo].[DocumentFolders]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentFolders] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [ParentId] uniqueidentifier NULL,
                    [IsDeleted] bit NOT NULL CONSTRAINT [DF_DocumentFolders_IsDeleted] DEFAULT (0),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentFolders] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentFolders_DocumentFolders_ParentId]
                        FOREIGN KEY ([ParentId]) REFERENCES [dbo].[DocumentFolders]([Id]) ON DELETE NO ACTION
                );
                CREATE UNIQUE INDEX [IX_DocumentFolders_ParentId_Name_IsDeleted]
                    ON [dbo].[DocumentFolders]([ParentId], [Name], [IsDeleted])
                    WHERE [IsDeleted] = 0;
                CREATE INDEX [IX_DocumentFolders_ParentId_CreatedAtUtc]
                    ON [dbo].[DocumentFolders]([ParentId], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Documents] (
                    [Id] uniqueidentifier NOT NULL,
                    [FolderId] uniqueidentifier NOT NULL,
                    [Title] nvarchar(300) NOT NULL,
                    [Category] nvarchar(120) NOT NULL,
                    [DocumentDateUtc] datetime2 NULL,
                    [ReferenceNumber] nvarchar(80) NULL,
                    [ManualReferenceNumber] nvarchar(80) NULL,
                    [AccessLevel] int NOT NULL,
                    [IsDeleted] bit NOT NULL CONSTRAINT [DF_Documents_IsDeleted] DEFAULT (0),
                    [OwnerUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_Documents_DocumentFolders_FolderId]
                        FOREIGN KEY ([FolderId]) REFERENCES [dbo].[DocumentFolders]([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_Documents_FolderId_IsDeleted_UpdatedAtUtc]
                    ON [dbo].[Documents]([FolderId], [IsDeleted], [UpdatedAtUtc]);
                CREATE INDEX [IX_Documents_OwnerUserId_UpdatedAtUtc]
                    ON [dbo].[Documents]([OwnerUserId], [UpdatedAtUtc]);
                CREATE INDEX [IX_Documents_Category_AccessLevel_UpdatedAtUtc]
                    ON [dbo].[Documents]([Category], [AccessLevel], [UpdatedAtUtc]);
            END
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.Documents', 'ManualReferenceNumber') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Documents] ADD [ManualReferenceNumber] nvarchar(80) NULL;
            END
            IF OBJECT_ID(N'[dbo].[Documents]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.Documents', 'Description') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Documents] ADD [Description] nvarchar(2000) NULL;
            END

            IF OBJECT_ID(N'[dbo].[DocumentVersions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentVersions] (
                    [Id] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [VersionNumber] int NOT NULL,
                    [OriginalFileName] nvarchar(260) NOT NULL,
                    [StoredPath] nvarchar(500) NOT NULL,
                    [Extension] nvarchar(16) NOT NULL,
                    [SizeBytes] bigint NOT NULL,
                    [ContentHashSha256] nvarchar(64) NULL,
                    [ChangeLog] nvarchar(500) NULL,
                    [UploadedByUserId] uniqueidentifier NOT NULL,
                    [UploadedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentVersions] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentVersions_Documents_DocumentId]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_DocumentVersions_DocumentId_VersionNumber]
                    ON [dbo].[DocumentVersions]([DocumentId], [VersionNumber]);
                CREATE INDEX [IX_DocumentVersions_DocumentId_UploadedAtUtc]
                    ON [dbo].[DocumentVersions]([DocumentId], [UploadedAtUtc]);
            END
            IF OBJECT_ID(N'[dbo].[DocumentVersions]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.DocumentVersions', 'ChangeLog') IS NULL
            BEGIN
                ALTER TABLE [dbo].[DocumentVersions] ADD [ChangeLog] nvarchar(500) NULL;
            END

            IF OBJECT_ID(N'[dbo].[DocumentVersionTexts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentVersionTexts] (
                    [DocumentVersionId] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [ExtractedText] nvarchar(max) NULL,
                    [NormalizedText] nvarchar(max) NULL,
                    [ProcessingStatus] int NOT NULL CONSTRAINT [DF_DocumentVersionTexts_Status] DEFAULT (0),
                    [AttemptCount] int NOT NULL CONSTRAINT [DF_DocumentVersionTexts_Attempts] DEFAULT (0),
                    [ErrorMessage] nvarchar(2000) NULL,
                    [CharCount] int NOT NULL CONSTRAINT [DF_DocumentVersionTexts_CharCount] DEFAULT (0),
                    [ProcessedAtUtc] datetime2 NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_DocumentVersionTexts_UpdatedAtUtc] DEFAULT (GETUTCDATE()),
                    CONSTRAINT [PK_DocumentVersionTexts] PRIMARY KEY ([DocumentVersionId]),
                    CONSTRAINT [FK_DocumentVersionTexts_DocumentVersions]
                        FOREIGN KEY ([DocumentVersionId]) REFERENCES [dbo].[DocumentVersions]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_DocumentVersionTexts_Documents]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_DocumentVersionTexts_DocumentId]
                    ON [dbo].[DocumentVersionTexts]([DocumentId]);
                CREATE INDEX [IX_DocumentVersionTexts_Status_UpdatedAtUtc]
                    ON [dbo].[DocumentVersionTexts]([ProcessingStatus], [UpdatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentTags]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentTags] (
                    [DocumentId] uniqueidentifier NOT NULL,
                    [Tag] nvarchar(80) NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentTags] PRIMARY KEY ([DocumentId], [Tag]),
                    CONSTRAINT [FK_DocumentTags_Documents_DocumentId]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_DocumentTags_Tag] ON [dbo].[DocumentTags]([Tag]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentSystemTags]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentSystemTags] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(80) NOT NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemTags_IsActive] DEFAULT (1),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentSystemTags] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentSystemTags_Name]
                    ON [dbo].[DocumentSystemTags]([Name])
                    WHERE [IsActive] = 1;
                CREATE INDEX [IX_DocumentSystemTags_IsActive_CreatedAtUtc]
                    ON [dbo].[DocumentSystemTags]([IsActive], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentSystemCategories]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentSystemCategories] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(80) NOT NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_DocumentSystemCategories_IsActive] DEFAULT (1),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentSystemCategories] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentSystemCategories_Name]
                    ON [dbo].[DocumentSystemCategories]([Name])
                    WHERE [IsActive] = 1;
                CREATE INDEX [IX_DocumentSystemCategories_IsActive_CreatedAtUtc]
                    ON [dbo].[DocumentSystemCategories]([IsActive], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentActivities]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentActivities] (
                    [Id] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [EventType] nvarchar(40) NOT NULL,
                    [Message] nvarchar(1000) NOT NULL,
                    [ActorUserId] uniqueidentifier NULL,
                    [IpAddress] nvarchar(45) NULL,
                    [UserAgent] nvarchar(500) NULL,
                    [Reason] nvarchar(500) NULL,
                    [OldValuesJson] nvarchar(max) NULL,
                    [NewValuesJson] nvarchar(max) NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentActivities] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentActivities_Documents_DocumentId]
                        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_DocumentActivities_DocumentId_CreatedAtUtc]
                    ON [dbo].[DocumentActivities]([DocumentId], [CreatedAtUtc]);
                CREATE INDEX [IX_DocumentActivities_EventType_CreatedAtUtc]
                    ON [dbo].[DocumentActivities]([EventType], [CreatedAtUtc]);
                CREATE INDEX [IX_DocumentActivities_ActorUserId_CreatedAtUtc]
                    ON [dbo].[DocumentActivities]([ActorUserId], [CreatedAtUtc]);
            END
            IF OBJECT_ID(N'[dbo].[DocumentActivities]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH('dbo.DocumentActivities', 'IpAddress') IS NULL
                    ALTER TABLE [dbo].[DocumentActivities] ADD [IpAddress] nvarchar(45) NULL;
                IF COL_LENGTH('dbo.DocumentActivities', 'UserAgent') IS NULL
                    ALTER TABLE [dbo].[DocumentActivities] ADD [UserAgent] nvarchar(500) NULL;
                IF COL_LENGTH('dbo.DocumentActivities', 'Reason') IS NULL
                    ALTER TABLE [dbo].[DocumentActivities] ADD [Reason] nvarchar(500) NULL;
                IF COL_LENGTH('dbo.DocumentActivities', 'OldValuesJson') IS NULL
                    ALTER TABLE [dbo].[DocumentActivities] ADD [OldValuesJson] nvarchar(max) NULL;
                IF COL_LENGTH('dbo.DocumentActivities', 'NewValuesJson') IS NULL
                    ALTER TABLE [dbo].[DocumentActivities] ADD [NewValuesJson] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentActivities_EventType_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[DocumentActivities]'))
                    CREATE INDEX [IX_DocumentActivities_EventType_CreatedAtUtc] ON [dbo].[DocumentActivities]([EventType], [CreatedAtUtc]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentActivities_ActorUserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[DocumentActivities]'))
                    CREATE INDEX [IX_DocumentActivities_ActorUserId_CreatedAtUtc] ON [dbo].[DocumentActivities]([ActorUserId], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentPermissionConfigs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentPermissionConfigs] (
                    [ResourceType] int NOT NULL,
                    [ResourceId] uniqueidentifier NOT NULL,
                    [InheritFromParent] bit NOT NULL CONSTRAINT [DF_DocumentPermissionConfigs_InheritFromParent] DEFAULT (1),
                    [UpdatedByUserId] uniqueidentifier NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentPermissionConfigs] PRIMARY KEY ([ResourceType], [ResourceId])
                );
                CREATE UNIQUE INDEX [IX_DocumentPermissionConfigs_ResourceType_ResourceId]
                    ON [dbo].[DocumentPermissionConfigs]([ResourceType], [ResourceId]);
                CREATE INDEX [IX_DocumentPermissionConfigs_UpdatedAtUtc]
                    ON [dbo].[DocumentPermissionConfigs]([UpdatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentPermissionEntries]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentPermissionEntries] (
                    [Id] uniqueidentifier NOT NULL,
                    [ResourceType] int NOT NULL,
                    [ResourceId] uniqueidentifier NOT NULL,
                    [SubjectType] int NOT NULL,
                    [SubjectId] uniqueidentifier NOT NULL,
                    [Level] int NOT NULL,
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentPermissionEntries] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentPermissionEntries_Resource_Subject]
                    ON [dbo].[DocumentPermissionEntries]([ResourceType], [ResourceId], [SubjectType], [SubjectId]);
                CREATE INDEX [IX_DocumentPermissionEntries_Resource_Level]
                    ON [dbo].[DocumentPermissionEntries]([ResourceType], [ResourceId], [Level]);
                CREATE INDEX [IX_DocumentPermissionEntries_Subject]
                    ON [dbo].[DocumentPermissionEntries]([SubjectType], [SubjectId]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentShareLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentShareLinks] (
                    [Id] uniqueidentifier NOT NULL,
                    [ResourceType] int NOT NULL,
                    [ResourceId] uniqueidentifier NOT NULL,
                    [Scope] int NOT NULL,
                    [Token] nvarchar(64) NOT NULL,
                    [SpecificSubjectIdsJson] nvarchar(max) NULL,
                    [ExpiresAtUtc] datetime2 NULL,
                    [IsRevoked] bit NOT NULL CONSTRAINT [DF_DocumentShareLinks_IsRevoked] DEFAULT (0),
                    [CreatedByUserId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentShareLinks] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentShareLinks_Token] ON [dbo].[DocumentShareLinks]([Token]);
                CREATE INDEX [IX_DocumentShareLinks_Resource]
                    ON [dbo].[DocumentShareLinks]([ResourceType], [ResourceId], [IsRevoked], [CreatedAtUtc]);
            END

            IF OBJECT_ID(N'[dbo].[Contracts]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.Contracts', 'IndexStatus') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Contracts]
                    ADD [IndexStatus] int NOT NULL CONSTRAINT [DF_Contracts_IndexStatus] DEFAULT (0);
            END

            IF OBJECT_ID(N'[dbo].[ContractTextIndexes]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ContractTextIndexes] (
                    [ContractId] uniqueidentifier NOT NULL,
                    [ExtractedText] nvarchar(max) NULL,
                    [NormalizedText] nvarchar(max) NULL,
                    [ExtractedAt] datetime2 NULL,
                    [LastError] nvarchar(2000) NULL,
                    [ExtractorVersion] nvarchar(20) NOT NULL CONSTRAINT [DF_ContractTextIndexes_ExtractorVersion] DEFAULT (N'1'),
                    [ContractVersionNumber] int NOT NULL CONSTRAINT [DF_ContractTextIndexes_Version] DEFAULT (1),
                    CONSTRAINT [PK_ContractTextIndexes] PRIMARY KEY ([ContractId]),
                    CONSTRAINT [FK_ContractTextIndexes_Contracts]
                        FOREIGN KEY ([ContractId]) REFERENCES [dbo].[Contracts]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_ContractTextIndexes_ExtractedAt]
                    ON [dbo].[ContractTextIndexes]([ExtractedAt]);
            END

            IF OBJECT_ID(N'[dbo].[FormFieldGroupTemplates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormFieldGroupTemplates] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [Description] nvarchar(1000) NULL,
                    [FieldsJson] nvarchar(max) NOT NULL CONSTRAINT [DF_FormFieldGroupTemplates_FieldsJson] DEFAULT (N'[]'),
                    [CreatedByUserId] uniqueidentifier NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_FormFieldGroupTemplates] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_FormFieldGroupTemplates_Name] ON [dbo].[FormFieldGroupTemplates]([Name]);
                CREATE INDEX [IX_FormFieldGroupTemplates_UpdatedAtUtc] ON [dbo].[FormFieldGroupTemplates]([UpdatedAtUtc] DESC);
            END

            IF COL_LENGTH('dbo.FormFieldGroupTemplates', 'IsDeleted') IS NULL
                ALTER TABLE [dbo].[FormFieldGroupTemplates] ADD [IsDeleted] bit NOT NULL
                    CONSTRAINT [DF_FormFieldGroupTemplates_IsDeleted] DEFAULT (0);

            IF COL_LENGTH('dbo.FormFieldGroupTemplates', 'DeletedAtUtc') IS NULL
                ALTER TABLE [dbo].[FormFieldGroupTemplates] ADD [DeletedAtUtc] datetime2 NULL;

            IF COL_LENGTH('dbo.FormFieldGroupTemplates', 'FieldCount') IS NULL
                ALTER TABLE [dbo].[FormFieldGroupTemplates] ADD [FieldCount] int NOT NULL
                    CONSTRAINT [DF_FormFieldGroupTemplates_FieldCount] DEFAULT (0);

            IF COL_LENGTH('dbo.FormFields', 'DefaultValue') IS NULL
                ALTER TABLE [dbo].[FormFields] ADD [DefaultValue] nvarchar(2000) NULL;

            IF COL_LENGTH('dbo.FormFields', 'IsReadOnly') IS NULL
                ALTER TABLE [dbo].[FormFields] ADD [IsReadOnly] bit NOT NULL
                    CONSTRAINT [DF_FormFields_IsReadOnly] DEFAULT (0);

            IF OBJECT_ID(N'[dbo].[FormWordTemplates]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH('dbo.FormWordTemplates', 'SignaturePlaceholderKey') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [SignaturePlaceholderKey] nvarchar(120) NULL;

                IF COL_LENGTH('dbo.FormWordTemplates', 'SignatureImagePath') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [SignatureImagePath] nvarchar(500) NULL;

                IF COL_LENGTH('dbo.FormWordTemplates', 'StampPlaceholderKey') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [StampPlaceholderKey] nvarchar(120) NULL;

                IF COL_LENGTH('dbo.FormWordTemplates', 'StampImagePath') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [StampImagePath] nvarchar(500) NULL;
            END

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

            IF OBJECT_ID(N'[dbo].[DocumentWorkflowTemplates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentWorkflowTemplates] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [StepsJson] nvarchar(max) NOT NULL,
                    [ActionDirectionKey] nvarchar(80) NULL,
                    [ActionDirectionLabel] nvarchar(200) NULL,
                    [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL CONSTRAINT [DF_DocumentWorkflowTemplates_ActionAssignees] DEFAULT ('[]'),
                    [CanvasLayoutJson] nvarchar(500) NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedByUserId] uniqueidentifier NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentWorkflowTemplates] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentWorkflowTemplates_Name] ON [dbo].[DocumentWorkflowTemplates]([Name]);
                CREATE INDEX [IX_DocumentWorkflowTemplates_IsActive_CreatedAtUtc] ON [dbo].[DocumentWorkflowTemplates]([IsActive], [CreatedAtUtc]);
            END

            IF COL_LENGTH('dbo.Documents', 'WorkflowStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStatus] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowStatus] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowTemplateId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowTemplateId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowName') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowName] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.Documents', 'StepsJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [StepsJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'CurrentStepOrder') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [CurrentStepOrder] int NOT NULL
                    CONSTRAINT [DF_Documents_CurrentStepOrder] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowScheduledStartAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowScheduledStartAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'PostApprovalJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [PostApprovalJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunCycle') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunCycle] int NOT NULL
                    CONSTRAINT [DF_Documents_WorkflowRunCycle] DEFAULT (0);
            IF COL_LENGTH('dbo.Documents', 'WorkflowRunsHistoryJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRunsHistoryJson] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Documents', 'WorkflowRejectionJson') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [WorkflowRejectionJson] nvarchar(max) NULL;

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

            IF COL_LENGTH('dbo.DocumentApprovalLinks', 'ReminderSmsSentAtUtc') IS NULL
                ALTER TABLE [dbo].[DocumentApprovalLinks] ADD [ReminderSmsSentAtUtc] datetime2 NULL;

            -- Document lifecycle / retention
            IF COL_LENGTH('dbo.Documents', 'ExpiresAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ExpiresAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'RetentionPolicyId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [RetentionPolicyId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'ArchiveTier') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchiveTier] int NOT NULL CONSTRAINT DF_Documents_ArchiveTier DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LifecycleStatus') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LifecycleStatus] int NOT NULL CONSTRAINT DF_Documents_LifecycleStatus DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'IsArchived') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsArchived] bit NOT NULL CONSTRAINT DF_Documents_IsArchived DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'ArchivedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ArchivedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHold') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHold] bit NOT NULL CONSTRAINT DF_Documents_LegalHold DEFAULT(0);
            IF COL_LENGTH('dbo.Documents', 'LegalHoldReason') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldReason] nvarchar(500) NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldStartedAtUtc') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldStartedAtUtc] datetime2 NULL;
            IF COL_LENGTH('dbo.Documents', 'LegalHoldByUserId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [LegalHoldByUserId] uniqueidentifier NULL;
            IF COL_LENGTH('dbo.Documents', 'IsObsolete') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [IsObsolete] bit NOT NULL CONSTRAINT DF_Documents_IsObsolete DEFAULT(0);
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
                ALTER TABLE [dbo].[Documents] ADD [LongTermRetention] bit NOT NULL CONSTRAINT DF_Documents_LongTermRetention DEFAULT(0);

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
                    [ExpirationWarningDays] int NOT NULL CONSTRAINT DF_DocumentRetentionPolicies_ExpirationWarningDays DEFAULT(30),
                    [LongTermRetention] bit NOT NULL CONSTRAINT DF_DocumentRetentionPolicies_LongTermRetention DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT DF_DocumentRetentionPolicies_IsActive DEFAULT(1),
                    [IsDefault] bit NOT NULL CONSTRAINT DF_DocumentRetentionPolicies_IsDefault DEFAULT(0),
                    [SortOrder] int NOT NULL CONSTRAINT DF_DocumentRetentionPolicies_SortOrder DEFAULT(0),
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentRetentionPolicies] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_DocumentRetentionPolicies_Name] ON [dbo].[DocumentRetentionPolicies]([Name]);
            END

            IF OBJECT_ID(N'[dbo].[DocumentLifecycleSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentLifecycleSettings] (
                    [Id] uniqueidentifier NOT NULL,
                    [DefaultRetentionPolicyId] uniqueidentifier NULL,
                    [AutoProcessEnabled] bit NOT NULL CONSTRAINT DF_DocumentLifecycleSettings_AutoProcessEnabled DEFAULT(1),
                    [DefaultExpirationWarningDays] int NOT NULL CONSTRAINT DF_DocumentLifecycleSettings_DefaultExpirationWarningDays DEFAULT(30),
                    [ProcessIntervalHours] int NOT NULL CONSTRAINT DF_DocumentLifecycleSettings_ProcessIntervalHours DEFAULT(6),
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [PK_DocumentLifecycleSettings] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentLifecycleSettings_DocumentRetentionPolicies_DefaultRetentionPolicyId]
                        FOREIGN KEY ([DefaultRetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL
                );
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_RetentionPolicyId' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_RetentionPolicyId] ON [dbo].[Documents]([RetentionPolicyId]) WHERE [RetentionPolicyId] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_IsArchived' AND object_id = OBJECT_ID(N'[dbo].[Documents]'))
                CREATE INDEX [IX_Documents_IsArchived] ON [dbo].[Documents]([IsArchived]);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Documents_DocumentRetentionPolicies_RetentionPolicyId')
                ALTER TABLE [dbo].[Documents] ADD CONSTRAINT [FK_Documents_DocumentRetentionPolicies_RetentionPolicyId]
                    FOREIGN KEY ([RetentionPolicyId]) REFERENCES [dbo].[DocumentRetentionPolicies]([Id]) ON DELETE SET NULL;
            """, ct);

        await BackfillFormFieldGroupFieldCountsAsync(db, logger, ct);

        logger.LogInformation("Database schema patch completed.");
    }

    private static async Task BackfillFormFieldGroupFieldCountsAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        var rows = await db.FormFieldGroupTemplates
            .Where(x => !x.IsDeleted && x.FieldsJson != "[]")
            .ToListAsync(ct);

        var updated = 0;
        foreach (var row in rows)
        {
            var count = FormFieldGroupJsonHelper.CountNonHeaderFields(row.FieldsJson);
            if (row.FieldCount == count) continue;
            row.FieldCount = count;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Backfilled FieldCount for {Count} field group template(s).", updated);
        }
    }

    /// <summary>حذف ستون‌های اشتباه و ایندکس‌های پرکاربرد (هم‌راستا با مایگریشن fix_schema_redundancy_and_indexes).</summary>
    public static async Task ApplyRedundancyCleanupAndPerformanceIndexesAsync(AppDbContext db, CancellationToken ct = default)
    {
        await ExecuteScriptAsync(db, SchemaCleanupSql.CleanupAndIndexes, ct);
    }
}
