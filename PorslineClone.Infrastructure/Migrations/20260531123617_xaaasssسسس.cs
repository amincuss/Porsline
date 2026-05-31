using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class xaaasssسسس : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicAnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicCollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitorKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicAnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicBanners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CtaLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CtaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicBanners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CoverImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CoverImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCollections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicPortalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortalEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SiteTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AboutText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FooterLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SocialLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShowViewCounts = table.Column<bool>(type: "bit", nullable: false),
                    ShowDownloadCounts = table.Column<bool>(type: "bit", nullable: false),
                    AllowDownloads = table.Column<bool>(type: "bit", nullable: false),
                    EnablePreviews = table.Column<bool>(type: "bit", nullable: false),
                    FeaturedSectionSize = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicPortalSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPublicProfiles",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublicDescription = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PublicCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicCollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CoverImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreviewAvailable = table.Column<bool>(type: "bit", nullable: false),
                    DownloadAllowed = table.Column<bool>(type: "bit", nullable: false),
                    PublicVisibilityStatus = table.Column<int>(type: "int", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishStartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishEndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false),
                    Pinned = table.Column<bool>(type: "bit", nullable: false),
                    SeoTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SeoKeywords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PublicViewCount = table.Column<long>(type: "bigint", nullable: false),
                    PublicDownloadCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPublicProfiles", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentPublicProfiles_DocumentVersions_PublicVersionId",
                        column: x => x.PublicVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DocumentPublicProfiles_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_DocumentPublicProfiles_PublicCategories_PublicCategoryId",
                        column: x => x.PublicCategoryId,
                        principalTable: "PublicCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DocumentPublicProfiles_PublicCollections_PublicCollectionId",
                        column: x => x.PublicCollectionId,
                        principalTable: "PublicCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_Language",
                table: "DocumentPublicProfiles",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_PublicCategoryId_Featured_Pinned",
                table: "DocumentPublicProfiles",
                columns: new[] { "PublicCategoryId", "Featured", "Pinned" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_PublicCollectionId_PublishedAtUtc",
                table: "DocumentPublicProfiles",
                columns: new[] { "PublicCollectionId", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_PublicVersionId",
                table: "DocumentPublicProfiles",
                column: "PublicVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_PublicVisibilityStatus_PublishedAtUtc",
                table: "DocumentPublicProfiles",
                columns: new[] { "PublicVisibilityStatus", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_PublishStartAtUtc_PublishEndAtUtc",
                table: "DocumentPublicProfiles",
                columns: new[] { "PublishStartAtUtc", "PublishEndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPublicProfiles_Slug",
                table: "DocumentPublicProfiles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicAnalyticsEvents_DocumentId_EventType_CreatedAtUtc",
                table: "PublicAnalyticsEvents",
                columns: new[] { "DocumentId", "EventType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicAnalyticsEvents_VisitorKey_DocumentId_EventType_CreatedAtUtc",
                table: "PublicAnalyticsEvents",
                columns: new[] { "VisitorKey", "DocumentId", "EventType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicBanners_IsActive_SortOrder",
                table: "PublicBanners",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCategories_IsActive_SortOrder",
                table: "PublicCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCategories_Slug",
                table: "PublicCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicCollections_IsActive_Featured_SortOrder",
                table: "PublicCollections",
                columns: new[] { "IsActive", "Featured", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCollections_Slug",
                table: "PublicCollections",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPublicProfiles");

            migrationBuilder.DropTable(
                name: "PublicAnalyticsEvents");

            migrationBuilder.DropTable(
                name: "PublicBanners");

            migrationBuilder.DropTable(
                name: "PublicPortalSettings");

            migrationBuilder.DropTable(
                name: "PublicCategories");

            migrationBuilder.DropTable(
                name: "PublicCollections");
        }
    }
}
