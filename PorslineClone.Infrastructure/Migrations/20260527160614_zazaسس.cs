using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class zazaسس : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualReferenceNumber",
                table: "Documents",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentSystemTags",
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
                    table.PrimaryKey("PK_DocumentSystemTags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSystemTags_IsActive_CreatedAtUtc",
                table: "DocumentSystemTags",
                columns: new[] { "IsActive", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSystemTags_Name",
                table: "DocumentSystemTags",
                column: "Name",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSystemTags");

            migrationBuilder.DropColumn(
                name: "ManualReferenceNumber",
                table: "Documents");
        }
    }
}
