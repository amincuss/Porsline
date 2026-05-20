using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class klaa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: DatabaseSchemaPatcher may have added these columns before EF migration runs.
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.InboxMessages', 'IsArchived') IS NULL
                    ALTER TABLE [dbo].[InboxMessages] ADD [IsArchived] bit NOT NULL
                        CONSTRAINT [DF_InboxMessages_IsArchived] DEFAULT (0);

                IF COL_LENGTH('dbo.InboxMessages', 'ReadAtUtc') IS NULL
                    ALTER TABLE [dbo].[InboxMessages] ADD [ReadAtUtc] datetime2 NULL;

                IF COL_LENGTH('dbo.AspNetUsers', 'Gender') IS NULL
                    ALTER TABLE [dbo].[AspNetUsers] ADD [Gender] int NULL;

                IF COL_LENGTH('dbo.AspNetUsers', 'PersonnelCode') IS NULL
                    ALTER TABLE [dbo].[AspNetUsers] ADD [PersonnelCode] nvarchar(30) NULL;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_AspNetUsers_PersonnelCode'
                      AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
                BEGIN
                    CREATE UNIQUE INDEX [IX_AspNetUsers_PersonnelCode]
                        ON [dbo].[AspNetUsers]([PersonnelCode])
                        WHERE [PersonnelCode] IS NOT NULL AND [PersonnelCode] <> '';
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_AspNetUsers_PersonnelCode'
                      AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
                    DROP INDEX [IX_AspNetUsers_PersonnelCode] ON [dbo].[AspNetUsers];

                IF COL_LENGTH('dbo.InboxMessages', 'IsArchived') IS NOT NULL
                BEGIN
                    DECLARE @dfInboxArchived nvarchar(200);
                    SELECT @dfInboxArchived = d.name
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID(N'[dbo].[InboxMessages]') AND c.name = N'IsArchived';
                    IF @dfInboxArchived IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[InboxMessages] DROP CONSTRAINT [' + @dfInboxArchived + ']');
                    ALTER TABLE [dbo].[InboxMessages] DROP COLUMN [IsArchived];
                END

                IF COL_LENGTH('dbo.InboxMessages', 'ReadAtUtc') IS NOT NULL
                    ALTER TABLE [dbo].[InboxMessages] DROP COLUMN [ReadAtUtc];

                IF COL_LENGTH('dbo.AspNetUsers', 'Gender') IS NOT NULL
                    ALTER TABLE [dbo].[AspNetUsers] DROP COLUMN [Gender];

                IF COL_LENGTH('dbo.AspNetUsers', 'PersonnelCode') IS NOT NULL
                    ALTER TABLE [dbo].[AspNetUsers] DROP COLUMN [PersonnelCode];
                """);
        }
    }
}
