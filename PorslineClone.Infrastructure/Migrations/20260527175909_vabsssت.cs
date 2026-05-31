using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vabsssت : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Documents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Documents");
        }
    }
}
