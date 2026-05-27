using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vsbsss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Body",
                table: "InboxMessages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileName",
                table: "InboxMessages",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "InboxMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHtml",
                table: "InboxMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SenderUserId",
                table: "InboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_SenderUserId_CreatedAtUtc",
                table: "InboxMessages",
                columns: new[] { "SenderUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_SenderUserId_CreatedAtUtc",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentFileName",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "IsHtml",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "SenderUserId",
                table: "InboxMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Body",
                table: "InboxMessages",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
