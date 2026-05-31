/*
  Document full-text search — SQL Server setup
  Run once per database (adjust [PorslineClone] name).

  Prerequisites:
  - SQL Server Full-Text Search feature installed
  - DocumentVersionTexts table exists (see DatabaseSchemaPatcher or EF migration)
*/

USE [PorslineClone];
GO

-- ---------------------------------------------------------------------------
-- 1) Table (if not created by app patcher)
-- ---------------------------------------------------------------------------
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
GO

-- ---------------------------------------------------------------------------
-- 2) Full-Text Catalog (Persian + English word breakers)
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ftc_Documents')
BEGIN
    CREATE FULLTEXT CATALOG [ftc_Documents] AS DEFAULT;
END
GO

-- PK must be unique, single-column — DocumentVersionTexts.DocumentVersionId is PK
IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_indexes fi
    INNER JOIN sys.tables t ON fi.object_id = t.object_id
    WHERE t.name = N'DocumentVersionTexts'
)
BEGIN
    CREATE FULLTEXT INDEX ON [dbo].[DocumentVersionTexts]([NormalizedText] LANGUAGE 1065)
        KEY INDEX [PK_DocumentVersionTexts]
        ON [ftc_Documents]
        WITH CHANGE_TRACKING AUTO, STOPLIST = SYSTEM;
    -- LANGUAGE 1065 = Persian (Farsi). Use 1033 for English-only columns if needed.
END
GO

-- ---------------------------------------------------------------------------
-- 3) Sample queries
-- ---------------------------------------------------------------------------

-- CONTAINS (exact phrase / prefix)
/*
DECLARE @q nvarchar(200) = N'"قرارداد" OR قرارداد*';
SELECT d.Id, d.Title, dv.VersionNumber, ft.[Rank]
FROM CONTAINSTABLE([dbo].[DocumentVersionTexts], [NormalizedText], @q) AS ft
INNER JOIN [dbo].[DocumentVersionTexts] t ON t.DocumentVersionId = ft.[KEY]
INNER JOIN [dbo].[DocumentVersions] dv ON dv.Id = t.DocumentVersionId
INNER JOIN [dbo].[Documents] d ON d.Id = t.DocumentId
WHERE d.IsDeleted = 0 AND t.ProcessingStatus = 2
ORDER BY ft.[Rank] DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
*/

-- FREETEXT (looser)
/*
DECLARE @q nvarchar(200) = N'بیمه نامه';
SELECT d.Id, d.Title, ft.[Rank]
FROM FREETEXTTABLE([dbo].[DocumentVersionTexts], [NormalizedText], @q) AS ft
INNER JOIN [dbo].[DocumentVersionTexts] t ON t.DocumentVersionId = ft.[KEY]
INNER JOIN [dbo].[Documents] d ON d.Id = t.DocumentId
WHERE d.IsDeleted = 0
ORDER BY ft.[Rank] DESC;
*/

-- Snippet (SUBSTRING around first match — simplified)
/*
DECLARE @term nvarchar(100) = N'قرارداد';
SELECT TOP 20
    d.Id,
    d.Title,
    LEFT(t.NormalizedText, 400) AS Preview
FROM [dbo].[DocumentVersionTexts] t
INNER JOIN [dbo].[Documents] d ON d.Id = t.DocumentId
WHERE t.ProcessingStatus = 2
  AND t.NormalizedText LIKE N'%' + @term + N'%'
ORDER BY d.UpdatedAtUtc DESC;
*/

GO
