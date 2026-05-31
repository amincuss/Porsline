using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class zaza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFolders_DocumentFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPermissionConfigs",
                columns: table => new
                {
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InheritFromParent = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPermissionConfigs", x => new { x.ResourceType, x.ResourceId });
                });

            migrationBuilder.CreateTable(
                name: "DocumentPermissionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPermissionEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SpecificSubjectIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentShareLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DocumentDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AccessLevel = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_DocumentFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentActivities_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTags",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTags", x => new { x.DocumentId, x.Tag });
                    table.ForeignKey(
                        name: "FK_DocumentTags_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHashSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ChangeLog = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentActivities_ActorUserId_CreatedAtUtc",
                table: "DocumentActivities",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentActivities_DocumentId_CreatedAtUtc",
                table: "DocumentActivities",
                columns: new[] { "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentActivities_EventType_CreatedAtUtc",
                table: "DocumentActivities",
                columns: new[] { "EventType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_ParentId_CreatedAtUtc",
                table: "DocumentFolders",
                columns: new[] { "ParentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_ParentId_Name_IsDeleted",
                table: "DocumentFolders",
                columns: new[] { "ParentId", "Name", "IsDeleted" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissionConfigs_ResourceType_ResourceId",
                table: "DocumentPermissionConfigs",
                columns: new[] { "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissionConfigs_UpdatedAtUtc",
                table: "DocumentPermissionConfigs",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissionEntries_ResourceType_ResourceId_Level",
                table: "DocumentPermissionEntries",
                columns: new[] { "ResourceType", "ResourceId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissionEntries_ResourceType_ResourceId_SubjectType_SubjectId",
                table: "DocumentPermissionEntries",
                columns: new[] { "ResourceType", "ResourceId", "SubjectType", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissionEntries_SubjectType_SubjectId",
                table: "DocumentPermissionEntries",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Category_AccessLevel_UpdatedAtUtc",
                table: "Documents",
                columns: new[] { "Category", "AccessLevel", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FolderId_IsDeleted_UpdatedAtUtc",
                table: "Documents",
                columns: new[] { "FolderId", "IsDeleted", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerUserId_UpdatedAtUtc",
                table: "Documents",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShareLinks_ResourceType_ResourceId_IsRevoked_CreatedAtUtc",
                table: "DocumentShareLinks",
                columns: new[] { "ResourceType", "ResourceId", "IsRevoked", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShareLinks_Token",
                table: "DocumentShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_Tag",
                table: "DocumentTags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_UploadedAtUtc",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_VersionNumber",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentActivities");

            migrationBuilder.DropTable(
                name: "DocumentPermissionConfigs");

            migrationBuilder.DropTable(
                name: "DocumentPermissionEntries");

            migrationBuilder.DropTable(
                name: "DocumentShareLinks");

            migrationBuilder.DropTable(
                name: "DocumentTags");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentFolders");
        }
    }
}
