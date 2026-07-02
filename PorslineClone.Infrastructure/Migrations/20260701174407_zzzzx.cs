using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class zzzzx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 60),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamForms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ParticipantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ParticipantMobile = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamLinks_ExamForms_ExamFormId",
                        column: x => x.ExamFormId,
                        principalTable: "ExamForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_ExamForms_ExamFormId",
                        column: x => x.ExamFormId,
                        principalTable: "ExamForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAutoSubmitted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSubmissions_ExamForms_ExamFormId",
                        column: x => x.ExamFormId,
                        principalTable: "ExamForms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamSubmissions_ExamLinks_ExamLinkId",
                        column: x => x.ExamLinkId,
                        principalTable: "ExamLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamForms_UpdatedAtUtc",
                table: "ExamForms",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExamLinks_Code",
                table: "ExamLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamLinks_ExamFormId_IsActive",
                table: "ExamLinks",
                columns: new[] { "ExamFormId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_ExamFormId_SortOrder",
                table: "ExamQuestions",
                columns: new[] { "ExamFormId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubmissions_ExamFormId",
                table: "ExamSubmissions",
                column: "ExamFormId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubmissions_ExamLinkId",
                table: "ExamSubmissions",
                column: "ExamLinkId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamQuestions");

            migrationBuilder.DropTable(
                name: "ExamSubmissions");

            migrationBuilder.DropTable(
                name: "ExamLinks");

            migrationBuilder.DropTable(
                name: "ExamForms");
        }
    }
}
