/*
  Contract full-text search — SQL Server setup
  Run once per database (adjust [PorslineClone] name).

  Prerequisites:
  - SQL Server Full-Text Search feature installed
  - ContractTextIndexes table exists (DatabaseSchemaPatcher or EF)
*/

USE [PorslineClone];
GO

IF OBJECT_ID(N'[dbo].[ContractTextIndexes]', N'U') IS NULL
BEGIN
    RAISERROR(N'ContractTextIndexes table missing. Start API with ApplySchemaPatch enabled.', 16, 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ftc_Contracts')
BEGIN
    CREATE FULLTEXT CATALOG [ftc_Contracts] AS DEFAULT;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_indexes fi
    INNER JOIN sys.tables t ON fi.object_id = t.object_id
    WHERE t.name = N'ContractTextIndexes'
)
BEGIN
    CREATE FULLTEXT INDEX ON [dbo].[ContractTextIndexes]([NormalizedText] LANGUAGE 1065)
        KEY INDEX [PK_ContractTextIndexes]
        ON [ftc_Contracts]
        WITH CHANGE_TRACKING AUTO, STOPLIST = SYSTEM;
    -- LANGUAGE 1065 = Persian. Use 0 (Neutral) if word breaking is limited on your SQL Server build.
END
GO

-- Sample CONTAINS query
/*
DECLARE @q nvarchar(200) = N'"قرارداد" OR قرارداد*';
SELECT c.Id, c.Title, c.ContractNumber, ft.[Rank]
FROM CONTAINSTABLE([dbo].[ContractTextIndexes], [NormalizedText], @q) AS ft
INNER JOIN [dbo].[ContractTextIndexes] t ON t.ContractId = ft.[KEY]
INNER JOIN [dbo].[Contracts] c ON c.Id = t.ContractId
WHERE c.IsSoftDeleted = 0 AND c.IndexStatus = 2
ORDER BY ft.[Rank] DESC;
*/
GO
