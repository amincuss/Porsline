using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class fix_security_settings_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SecuritySettings', 'AnonymousLinkExpiryDays') IS NULL
                    ALTER TABLE [dbo].[SecuritySettings] ADD [AnonymousLinkExpiryDays] int NOT NULL
                        CONSTRAINT [DF_SecuritySettings_AnonymousLinkExpiryDays] DEFAULT (7);
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SecuritySettings', 'DispatchLinkRequireOtp') IS NULL
                    ALTER TABLE [dbo].[SecuritySettings] ADD [DispatchLinkRequireOtp] bit NOT NULL
                        CONSTRAINT [DF_SecuritySettings_DispatchLinkRequireOtp] DEFAULT (0);
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SecuritySettings', 'AccessTokenLifetimeMinutes') IS NULL
                    ALTER TABLE [dbo].[SecuritySettings] ADD [AccessTokenLifetimeMinutes] int NOT NULL
                        CONSTRAINT [DF_SecuritySettings_AccessTokenLifetimeMinutes] DEFAULT (180);
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SecuritySettings', 'RefreshTokenLifetimeDays') IS NULL
                    ALTER TABLE [dbo].[SecuritySettings] ADD [RefreshTokenLifetimeDays] int NOT NULL
                        CONSTRAINT [DF_SecuritySettings_RefreshTokenLifetimeDays] DEFAULT (7);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SecuritySettings', 'RefreshTokenLifetimeDays') IS NOT NULL
                    ALTER TABLE [dbo].[SecuritySettings] DROP COLUMN [RefreshTokenLifetimeDays];
                IF COL_LENGTH('dbo.SecuritySettings', 'AccessTokenLifetimeMinutes') IS NOT NULL
                    ALTER TABLE [dbo].[SecuritySettings] DROP COLUMN [AccessTokenLifetimeMinutes];
                IF COL_LENGTH('dbo.SecuritySettings', 'DispatchLinkRequireOtp') IS NOT NULL
                    ALTER TABLE [dbo].[SecuritySettings] DROP COLUMN [DispatchLinkRequireOtp];
                IF COL_LENGTH('dbo.SecuritySettings', 'AnonymousLinkExpiryDays') IS NOT NULL
                    ALTER TABLE [dbo].[SecuritySettings] DROP COLUMN [AnonymousLinkExpiryDays];
                """);
        }
    }
}
