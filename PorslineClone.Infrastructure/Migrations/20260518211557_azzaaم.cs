using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azzaaم : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumberPrefix",
                table: "ContractSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "EN",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "CNT");

            migrationBuilder.UpdateData(
                table: "ContractSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DocumentNumberPrefix",
                value: "EN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumberPrefix",
                table: "ContractSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CNT",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "EN");

            migrationBuilder.UpdateData(
                table: "ContractSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DocumentNumberPrefix",
                value: "CNT");
        }
    }
}
