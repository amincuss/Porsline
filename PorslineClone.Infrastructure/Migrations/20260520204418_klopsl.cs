using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class klopsl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionKey') IS NULL
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionDirectionKey] nvarchar(80) NULL;

                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionLabel') IS NULL
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionDirectionLabel] nvarchar(200) NULL;

                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NULL
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] ADD [ActionAssigneeUserIdsJson] nvarchar(max) NOT NULL
                        CONSTRAINT [DF_ContractWorkflowTemplates_ActionAssignees] DEFAULT (N'[]');

                IF COL_LENGTH('dbo.Contracts', 'PostApprovalJson') IS NULL
                    ALTER TABLE [dbo].[Contracts] ADD [PostApprovalJson] nvarchar(max) NULL;
                """);

            migrationBuilder.Sql(
                """
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[ContractActionLinks]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[ContractActionLinks];

                IF COL_LENGTH('dbo.Contracts', 'PostApprovalJson') IS NOT NULL
                    ALTER TABLE [dbo].[Contracts] DROP COLUMN [PostApprovalJson];

                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionAssigneeUserIdsJson') IS NOT NULL
                BEGIN
                    DECLARE @df nvarchar(200);
                    SELECT @df = d.name
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID(N'[dbo].[ContractWorkflowTemplates]')
                      AND c.name = N'ActionAssigneeUserIdsJson';
                    IF @df IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[ContractWorkflowTemplates] DROP CONSTRAINT [' + @df + N']');
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] DROP COLUMN [ActionAssigneeUserIdsJson];
                END

                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionLabel') IS NOT NULL
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] DROP COLUMN [ActionDirectionLabel];

                IF COL_LENGTH('dbo.ContractWorkflowTemplates', 'ActionDirectionKey') IS NOT NULL
                    ALTER TABLE [dbo].[ContractWorkflowTemplates] DROP COLUMN [ActionDirectionKey];
                """);
        }
    }
}
