using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class azaaیثیثl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_TrackingCode",
                table: "FormSubmissions");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "FormSubmissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FormSubmissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TrackingCode",
                table: "FormSubmissions",
                column: "TrackingCode",
                unique: true,
                filter: "[TrackingCode] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_TrackingCode",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FormSubmissions");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TrackingCode",
                table: "FormSubmissions",
                column: "TrackingCode",
                unique: true,
                filter: "[TrackingCode] IS NOT NULL");
        }
    }
}
