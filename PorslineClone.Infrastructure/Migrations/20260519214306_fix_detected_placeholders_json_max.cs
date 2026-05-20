using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class fix_detected_placeholders_json_max : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.ContractDocumentTemplateVersions', 'DetectedPlaceholdersJson') IS NOT NULL
                   AND (SELECT max_length FROM sys.columns
                        WHERE object_id = OBJECT_ID(N'dbo.ContractDocumentTemplateVersions')
                          AND name = N'DetectedPlaceholdersJson') > 0
                BEGIN
                    ALTER TABLE [dbo].[ContractDocumentTemplateVersions]
                        ALTER COLUMN [DetectedPlaceholdersJson] nvarchar(max) NOT NULL;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.ContractDocumentTemplateVersions', 'DetectedPlaceholdersJson') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[ContractDocumentTemplateVersions]
                        ALTER COLUMN [DetectedPlaceholdersJson] nvarchar(4000) NOT NULL;
                END
                """);
        }
    }
}
