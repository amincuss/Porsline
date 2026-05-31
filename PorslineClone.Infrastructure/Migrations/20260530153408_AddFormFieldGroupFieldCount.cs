using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormFieldGroupFieldCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormFieldGroupTemplates', 'FieldCount') IS NULL
                    ALTER TABLE [dbo].[FormFieldGroupTemplates] ADD [FieldCount] int NOT NULL
                        CONSTRAINT [DF_FormFieldGroupTemplates_FieldCount] DEFAULT (0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.FormFieldGroupTemplates', 'FieldCount') IS NOT NULL
                BEGIN
                    DECLARE @df sysname;
                    SELECT @df = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.FormFieldGroupTemplates')
                      AND c.name = N'FieldCount';
                    IF @df IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[FormFieldGroupTemplates] DROP CONSTRAINT [' + @df + ']');
                    ALTER TABLE [dbo].[FormFieldGroupTemplates] DROP COLUMN [FieldCount];
                END
                """);
        }
    }
}
