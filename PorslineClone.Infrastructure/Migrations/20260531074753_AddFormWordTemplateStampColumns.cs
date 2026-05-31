using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations;

/// <summary>Adds columns that exist in the model but were missing from databases created before stamp support.</summary>
public partial class AddFormWordTemplateStampColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.FormWordTemplates', 'StampPlaceholderKey') IS NULL
                ALTER TABLE [dbo].[FormWordTemplates] ADD [StampPlaceholderKey] nvarchar(120) NULL;

            IF COL_LENGTH('dbo.FormWordTemplates', 'StampImagePath') IS NULL
                ALTER TABLE [dbo].[FormWordTemplates] ADD [StampImagePath] nvarchar(500) NULL;

            IF COL_LENGTH('dbo.FormFields', 'DefaultValue') IS NULL
                ALTER TABLE [dbo].[FormFields] ADD [DefaultValue] nvarchar(2000) NULL;

            IF COL_LENGTH('dbo.FormFields', 'IsReadOnly') IS NULL
                ALTER TABLE [dbo].[FormFields] ADD [IsReadOnly] bit NOT NULL
                    CONSTRAINT [DF_FormFields_IsReadOnly] DEFAULT (0);
            """);

        // Ensure signature columns exist on older FormWordTemplates rows (table predates Word export feature).
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[FormWordTemplates]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH('dbo.FormWordTemplates', 'SignaturePlaceholderKey') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [SignaturePlaceholderKey] nvarchar(120) NULL;

                IF COL_LENGTH('dbo.FormWordTemplates', 'SignatureImagePath') IS NULL
                    ALTER TABLE [dbo].[FormWordTemplates] ADD [SignatureImagePath] nvarchar(500) NULL;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.FormWordTemplates', 'StampImagePath') IS NOT NULL
                ALTER TABLE [dbo].[FormWordTemplates] DROP COLUMN [StampImagePath];

            IF COL_LENGTH('dbo.FormWordTemplates', 'StampPlaceholderKey') IS NOT NULL
                ALTER TABLE [dbo].[FormWordTemplates] DROP COLUMN [StampPlaceholderKey];
            """);
    }
}
