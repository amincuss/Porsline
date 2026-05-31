using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class klopsass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_Name",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplates_Name",
                table: "ContractDocumentTemplates");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "UserPositions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UserPositions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultValue",
                table: "FormFields",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "FormFields",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "FormFieldGroupTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FormFieldGroupTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ContractDocumentTemplateVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ContractDocumentTemplateVersions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ContractDocumentTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ContractDocumentTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_Name",
                table: "UserPositions",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplates_Name",
                table: "ContractDocumentTemplates",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_Name",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_ContractDocumentTemplates_Name",
                table: "ContractDocumentTemplates");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "UserPositions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UserPositions");

            migrationBuilder.DropColumn(
                name: "DefaultValue",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "FormFieldGroupTemplates");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FormFieldGroupTemplates");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ContractDocumentTemplateVersions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ContractDocumentTemplateVersions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ContractDocumentTemplates");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ContractDocumentTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_Name",
                table: "UserPositions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentTemplates_Name",
                table: "ContractDocumentTemplates",
                column: "Name",
                unique: true);
        }
    }
}
