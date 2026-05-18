using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azzzaبب : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_Key",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_SortOrder",
                table: "ContractDocumentTemplateFields");

            // SQL Server cannot reference a column in the same batch it was added (error 207).
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.ContractDocumentTemplateFields', 'VersionId') IS NULL
                    ALTER TABLE [ContractDocumentTemplateFields] ADD [VersionId] uniqueidentifier NULL;
                ELSE IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    WHERE t.name = N'ContractDocumentTemplateFields'
                      AND c.name = N'VersionId'
                      AND c.is_nullable = 0)
                    ALTER TABLE [ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE f
                SET f.[VersionId] = COALESCE(
                    t.[ActiveVersionId],
                    (SELECT TOP 1 v.[Id] FROM [ContractDocumentTemplateVersions] v
                     WHERE v.[TemplateId] = f.[TemplateId]
                     ORDER BY v.[VersionNumber] DESC))
                FROM [ContractDocumentTemplateFields] f
                INNER JOIN [ContractDocumentTemplates] t ON t.[Id] = f.[TemplateId]
                WHERE f.[VersionId] IS NULL
                   OR f.[VersionId] = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM [ContractDocumentTemplateFields]
                WHERE [VersionId] IS NULL
                   OR [VersionId] = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE [ContractDocumentTemplateFields] ALTER COLUMN [VersionId] uniqueidentifier NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId",
                table: "ContractDocumentTemplateFields",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_VersionId_Key",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "VersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_VersionId_SortOrder",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "VersionId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplateVersions_VersionId",
                table: "ContractDocumentTemplateFields",
                column: "VersionId",
                principalTable: "ContractDocumentTemplateVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateFields",
                column: "TemplateId",
                principalTable: "ContractDocumentTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplateVersions_VersionId",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplateFields_VersionId_Key",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplateFields_VersionId_SortOrder",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.DropColumn(
                name: "VersionId",
                table: "ContractDocumentTemplateFields");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_Key",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplateFields_TemplateId_SortOrder",
                table: "ContractDocumentTemplateFields",
                columns: new[] { "TemplateId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_ContractDocumentTemplateFields_ContractDocumentTemplates_TemplateId",
                table: "ContractDocumentTemplateFields",
                column: "TemplateId",
                principalTable: "ContractDocumentTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
