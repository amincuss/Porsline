using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddFormWordTemplateStamp : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StampImagePath",
            table: "FormWordTemplates",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StampPlaceholderKey",
            table: "FormWordTemplates",
            type: "nvarchar(120)",
            maxLength: 120,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StampImagePath", table: "FormWordTemplates");
        migrationBuilder.DropColumn(name: "StampPlaceholderKey", table: "FormWordTemplates");
    }
}
