using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vsbs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: schema patcher may have already added these columns on existing servers.
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SmsSettings', 'FormSubmissionTrackingSmsEnabled') IS NULL
                    ALTER TABLE [dbo].[SmsSettings] ADD [FormSubmissionTrackingSmsEnabled] bit NOT NULL
                        CONSTRAINT [DF_SmsSettings_FormSubmissionTrackingSmsEnabled] DEFAULT (0);

                UPDATE [dbo].[SmsSettings]
                SET [FormSubmissionTrackingSmsEnabled] = 1
                WHERE [Id] = 1;
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'TrackingCode') IS NULL
                    ALTER TABLE [dbo].[FormSubmissions] ADD [TrackingCode] nvarchar(32) NULL;
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'TrackingCode') IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM sys.indexes
                       WHERE name = N'IX_FormSubmissions_TrackingCode'
                         AND object_id = OBJECT_ID(N'dbo.FormSubmissions'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_FormSubmissions_TrackingCode]
                        ON [dbo].[FormSubmissions]([TrackingCode])
                        WHERE [TrackingCode] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_FormSubmissions_TrackingCode'
                      AND object_id = OBJECT_ID(N'dbo.FormSubmissions'))
                    DROP INDEX [IX_FormSubmissions_TrackingCode] ON [dbo].[FormSubmissions];
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormSubmissions', 'TrackingCode') IS NOT NULL
                    ALTER TABLE [dbo].[FormSubmissions] DROP COLUMN [TrackingCode];
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SmsSettings', 'FormSubmissionTrackingSmsEnabled') IS NOT NULL
                BEGIN
                    DECLARE @df sysname;
                    SELECT @df = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SmsSettings')
                      AND c.name = N'FormSubmissionTrackingSmsEnabled';
                    IF @df IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[SmsSettings] DROP CONSTRAINT [' + @df + N']');
                    ALTER TABLE [dbo].[SmsSettings] DROP COLUMN [FormSubmissionTrackingSmsEnabled];
                END
                """);
        }
    }
}
