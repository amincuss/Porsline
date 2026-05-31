using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vabsss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentSystemCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSystemCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSystemCategories_IsActive_CreatedAtUtc",
                table: "DocumentSystemCategories",
                columns: new[] { "IsActive", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSystemCategories_Name",
                table: "DocumentSystemCategories",
                column: "Name",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSystemCategories");
        }
    }
}
