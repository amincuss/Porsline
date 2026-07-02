using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class sss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormSubmissionExcelExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UngroupedOnly = table.Column<bool>(type: "bit", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HangfireJobId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionExcelExportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsPatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IconColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Template = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlaceholdersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsPatterns", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                column: "Order",
                value: 3);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000005"),
                column: "Order",
                value: 4);

            migrationBuilder.InsertData(
                table: "MenuItems",
                columns: new[] { "Id", "Icon", "IconColor", "Key", "Order", "ParentId", "Route", "Title" },
                values: new object[] { new Guid("30000000-0000-0000-0000-000000000007"), "MessagesSquare", "#14B8A6", "settings.sms-patterns", 2, new Guid("30000000-0000-0000-0000-000000000002"), "/admin/settings/sms-patterns", "پترن پیامک" });

            migrationBuilder.InsertData(
                table: "RoleMenus",
                columns: new[] { "MenuId", "RoleId" },
                values: new object[] { new Guid("30000000-0000-0000-0000-000000000007"), new Guid("10000000-0000-0000-0000-000000000001") });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionExcelExportJobs_CreatedAtUtc",
                table: "FormSubmissionExcelExportJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionExcelExportJobs_CreatedByUserId",
                table: "FormSubmissionExcelExportJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionExcelExportJobs_FormId",
                table: "FormSubmissionExcelExportJobs",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionExcelExportJobs_Status",
                table: "FormSubmissionExcelExportJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SmsPatterns_Category",
                table: "SmsPatterns",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SmsPatterns_Key",
                table: "SmsPatterns",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsPatterns_SortOrder",
                table: "SmsPatterns",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormSubmissionExcelExportJobs");

            migrationBuilder.DropTable(
                name: "SmsPatterns");

            migrationBuilder.DeleteData(
                table: "RoleMenus",
                keyColumns: new[] { "MenuId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000007"), new Guid("10000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "MenuItems",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000007"));

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                column: "Order",
                value: 2);

            migrationBuilder.UpdateData(
                table: "MenuItems",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000005"),
                column: "Order",
                value: 3);
        }
    }
}
