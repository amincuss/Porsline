using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Auth;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class AuthService(UserManager<AppUser> userManager, RoleManager<AppRole> roleManager, AppDbContext db, ISmsSender smsSender, IOptions<JwtOptions> jwtOptions) : IAuthService
{
    public async Task<OtpSendResultDto> SendOtpAsync(string mobileNumber, string ipAddress, CancellationToken cancellationToken = default)
    {
        var settings = await GetSecuritySettingsAsync(cancellationToken);
        if (await IsRateLimitedAsync(ipAddress, "otp_send", settings, cancellationToken))
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_send", false, cancellationToken);
            return new OtpSendResultDto(false, null);
        }

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == mobileNumber && !x.IsSoftDeleted && x.IsActive, cancellationToken);
        if (user is null)
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_send", false, cancellationToken);
            return new OtpSendResultDto(false, null);
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        db.OtpCodes.Add(new OtpCode { Id = Guid.NewGuid(), MobileNumber = mobileNumber, Code = code, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2) });

        bool isDev = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);

        bool isSent;
        if (isDev)
        {
            // در محیط Development پیامک OTP ارسال نمی‌شود — کد به صفحه confirm برمی‌گردد
            isSent = true;
        }
        else
        {
            isSent = await smsSender.SendSmsAsync(new SmsRequest(mobileNumber, $"کد ورود شما: {code}"), cancellationToken);
        }

        await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_send", isSent, cancellationToken);
        return new OtpSendResultDto(isSent, isDev ? code : null);
    }

    public async Task<AuthResponseDto?> VerifyOtpAsync(string mobileNumber, string code, string ipAddress, CancellationToken cancellationToken = default)
    {
        var settings = await GetSecuritySettingsAsync(cancellationToken);
        if (await IsRateLimitedAsync(ipAddress, "otp_verify", settings, cancellationToken))
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_verify", false, cancellationToken);
            return null;
        }

        if (await IsLockedOutAsync(mobileNumber, settings, cancellationToken))
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_verify", false, cancellationToken);
            return null;
        }

        var otp = await db.OtpCodes.Where(x => x.MobileNumber == mobileNumber && !x.IsUsed && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (otp is null || otp.Code != code)
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_verify", false, cancellationToken);
            return null;
        }

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == mobileNumber && !x.IsSoftDeleted && x.IsActive, cancellationToken);
        if (user is null)
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_verify", false, cancellationToken);
            return null;
        }

        otp.IsUsed = true;
        await AddLoginAttemptAsync(mobileNumber, ipAddress, "otp_verify", true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponseDto?> LoginWithPasswordAsync(string mobileNumber, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        var settings = await GetSecuritySettingsAsync(cancellationToken);
        if (await IsRateLimitedAsync(ipAddress, "pwd_login", settings, cancellationToken))
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "pwd_login", false, cancellationToken);
            return null;
        }

        if (await IsLockedOutAsync(mobileNumber, settings, cancellationToken))
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "pwd_login", false, cancellationToken);
            return null;
        }

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == mobileNumber && !x.IsSoftDeleted && x.IsActive, cancellationToken);
        if (user is null)
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "pwd_login", false, cancellationToken);
            return null;
        }

        var valid = await userManager.CheckPasswordAsync(user, password);
        if (!valid)
        {
            await AddLoginAttemptAsync(mobileNumber, ipAddress, "pwd_login", false, cancellationToken);
            return null;
        }

        await AddLoginAttemptAsync(mobileNumber, ipAddress, "pwd_login", true, cancellationToken);
        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tokenHash = HashToken(refreshToken);
        var stored = await db.RefreshTokens
            .Where(x => x.TokenHash == tokenHash)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= now)
            return null;

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.Id == stored.UserId && !x.IsSoftDeleted && x.IsActive, cancellationToken);
        if (user is null) return null;

        stored.RevokedAtUtc = now;
        var nextRawToken = GenerateSecureToken();
        stored.ReplacedByTokenHash = HashToken(nextRawToken);

        var security = await GetSecuritySettingsAsync(cancellationToken);
        var refreshDays = SecuritySettingsHelper.ClampRefreshDays(security.RefreshTokenLifetimeDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = stored.ReplacedByTokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(refreshDays)
        });

        await db.SaveChangesAsync(cancellationToken);
        return await BuildAuthResponseAsync(user, cancellationToken, nextRawToken);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = await db.RefreshTokens
            .Where(x => x.TokenHash == tokenHash)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null || stored.RevokedAtUtc is not null) return false;
        stored.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<AuthResponseDto> BuildAuthResponseAsync(AppUser user, CancellationToken cancellationToken, string? rawRefreshToken = null)
    {
        var roleNames = await userManager.GetRolesAsync(user);
        var roleName = roleNames.FirstOrDefault() ?? "User";

        // جمع‌آوری permissions از تمام role‌های کاربر
        var roleIds = await roleManager.Roles
            .Where(x => roleNames.Contains(x.Name!))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var perms = await db.RolePermissions
            .Where(x => roleIds.Contains(x.RoleId))
            .Select(x => x.Permission!.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        var jwt = jwtOptions.Value;
        var security = await GetSecuritySettingsAsync(cancellationToken);
        var accessMinutes = SecuritySettingsHelper.ClampAccessMinutes(security.AccessTokenLifetimeMinutes);
        var refreshDays = SecuritySettingsHelper.ClampRefreshDays(security.RefreshTokenLifetimeDays);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.Role, roleName)
        };
        foreach (var rn in roleNames) claims.Add(new Claim(ClaimTypes.Role, rn));
        claims.AddRange(perms.Select(x => new Claim("permission", x)));

        var token = new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(accessMinutes),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256));

        var refreshTokenValue = rawRefreshToken ?? GenerateSecureToken();
        var refreshExpire = DateTime.UtcNow.AddDays(refreshDays);

        if (rawRefreshToken is null)
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = HashToken(refreshTokenValue),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = refreshExpire
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshTokenValue,
            token.ValidTo,
            refreshExpire,
            $"{user.FirstName} {user.LastName}".Trim(),
            roleName);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private async Task<SecuritySettings> GetSecuritySettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.SecuritySettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null) return settings;

        settings = new SecuritySettings();
        db.SecuritySettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private async Task<bool> IsRateLimitedAsync(string ipAddress, string attemptType, SecuritySettings settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableRateLimiting) return false;

        var since = DateTime.UtcNow.AddMinutes(-1);
        var count = await db.LoginAttempts
            .Where(x => x.IpAddress == ipAddress && x.AttemptType == attemptType && x.CreatedAtUtc >= since)
            .CountAsync(cancellationToken);

        return count >= settings.MaxRequestsPerMinutePerIp;
    }

    private async Task<bool> IsLockedOutAsync(string mobileNumber, SecuritySettings settings, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddMinutes(-settings.LockoutMinutes);
        var failedCount = await db.LoginAttempts
            .Where(x => x.MobileNumber == mobileNumber && x.AttemptType == "otp_verify" && !x.IsSuccess && x.CreatedAtUtc >= since)
            .CountAsync(cancellationToken);

        return failedCount >= settings.MaxFailedOtpAttempts;
    }

    private async Task AddLoginAttemptAsync(string mobileNumber, string ipAddress, string attemptType, bool isSuccess, CancellationToken cancellationToken)
    {
        db.LoginAttempts.Add(new LoginAttempt
        {
            Id = Guid.NewGuid(),
            MobileNumber = mobileNumber,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            AttemptType = attemptType,
            IsSuccess = isSuccess,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}

public static class DbSeeder
{
    public static async Task EnsureReferenceDataAsync(AppDbContext db, RoleManager<AppRole> roleManager, CancellationToken cancellationToken = default)
    {
        // Roles
        var requiredRoles = new[]
        {
            new AppRole { Name = "Admin", DisplayName = "مدیر سیستم" },
            new AppRole { Name = "Expert", DisplayName = "کارشناس" }
        };
        foreach (var rr in requiredRoles)
        {
            var role = await roleManager.FindByNameAsync(rr.Name!);
            if (role is null)
            {
                await roleManager.CreateAsync(new AppRole
                {
                    Id = Guid.NewGuid(),
                    Name = rr.Name,
                    NormalizedName = rr.Name!.ToUpperInvariant(),
                    DisplayName = rr.DisplayName
                });
            }
            else if (role.DisplayName != rr.DisplayName)
            {
                role.DisplayName = rr.DisplayName;
                await roleManager.UpdateAsync(role);
            }
        }

        // Permissions (granular)
        var permissionNames = new[]
        {
            "users.read","users.read.all","users.add","users.import","users.update","users.delete",
            "users.access.read","users.access.update",
            "settings.read","settings.update","settings.delete",
            "roles.read","roles.update",
            "menus.view","profile.update","messages.read","messages.read.all","messages.send",
            "forms.read","forms.read.all","forms.add","forms.update","forms.delete",
            "forms.rules.read","forms.rules.update","forms.rules.delete",
            "forms.access.read","forms.access.read.all","forms.access.update",
            "approvals.read","approvals.update",
            "workflow-runs.read","workflow-runs.read.all","workflow-runs.update",
            "forms.archive.read","forms.archive.read.all",
            "contracts.archive.read","contracts.archive.read.all",
            "actions.read","actions.read.all","actions.update",
            "responders.read","responders.read.all","responders.add","responders.update","responders.delete","responders.send","responders.send.activation",
            "responders.userforms.delete","responders.userforms.workflow","responders.userforms.workflow.restart",
            "respondergroups.read","respondergroups.add","respondergroups.update","respondergroups.delete",
            "usergroups.read","usergroups.read.all","usergroups.add","usergroups.update","usergroups.delete",
            "contracts.read","contracts.read.all","contracts.add","contracts.update","contracts.delete",
            "contracts.settings.read","contracts.settings.update","contracts.settings.delete",
        }
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
        foreach (var name in permissionNames)
        {
            if (!await db.Permissions.AnyAsync(x => x.Name == name, cancellationToken))
                db.Permissions.Add(new Permission { Id = Guid.NewGuid(), Name = name });
        }
        await db.SaveChangesAsync(cancellationToken);

        // Menus
        var dashboardMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "dashboard", cancellationToken);
        if (dashboardMenu is null)
        {
            dashboardMenu = new MenuItem
            {
                Id = Guid.NewGuid(),
                Key = "dashboard",
                Title = "داشبورد",
                Icon = "LayoutDashboard",
                IconColor = "#6366F1",
                Route = "/admin",
                Order = -1
            };
            db.MenuItems.Add(dashboardMenu);
        }
        else
        {
            dashboardMenu.Title = "داشبورد";
            dashboardMenu.Icon = "LayoutDashboard";
            dashboardMenu.IconColor = "#6366F1";
            dashboardMenu.Route = "/admin";
            dashboardMenu.Order = -1;
            dashboardMenu.ParentId = null;
        }

        var formsMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "forms", cancellationToken);
        if (formsMenu is null)
        {
            formsMenu = new MenuItem { Id = Guid.NewGuid(), Key = "forms", Title = "فرم ساز", Icon = "LayoutTemplate", IconColor = "#10B981", Route = null, Order = 0 };
            db.MenuItems.Add(formsMenu);
        }
        else
        {
            formsMenu.Route = null;
            formsMenu.Icon = "LayoutTemplate";
            formsMenu.IconColor = "#10B981";
        }

        var usersMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "users", cancellationToken);
        if (usersMenu is null)
        {
            usersMenu = new MenuItem { Id = Guid.NewGuid(), Key = "users", Title = "مدیریت کاربران", Icon = "Users", IconColor = "#0EA5E9", Route = null, Order = 1 };
            db.MenuItems.Add(usersMenu);
        }
        else
        {
            usersMenu.Route = null;
            usersMenu.Icon = "Users";
            usersMenu.IconColor = "#0EA5E9";
        }

        var settingsMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "settings", cancellationToken);
        if (settingsMenu is null)
        {
            settingsMenu = new MenuItem { Id = Guid.NewGuid(), Key = "settings", Title = "تنظیمات سایت", Icon = "Settings", IconColor = "#F59E0B", Order = 2 };
            db.MenuItems.Add(settingsMenu);
        }
        await db.SaveChangesAsync(cancellationToken);

        settingsMenu = await db.MenuItems.FirstAsync(x => x.Key == "settings", cancellationToken);
        var requiredMenus = new[]
        {
            new MenuItem { Key = "settings.site", Title = "دامنه و لینک پیامک", Icon = "Settings", IconColor = "#059669", Route = "/admin/settings/site", Order = 1, ParentId = settingsMenu.Id },
            new MenuItem { Key = "settings.sms", Title = "تنظیمات پیامک", Icon = "MessageSquare", IconColor = "#8B5CF6", Route = "/admin/settings/sms", Order = 2, ParentId = settingsMenu.Id },
            new MenuItem { Key = "settings.security", Title = "تنظیمات امنیتی", Icon = "ShieldCheck", IconColor = "#EF4444", Route = "/admin/settings/security", Order = 3, ParentId = settingsMenu.Id },
            new MenuItem { Key = "settings.access", Title = "سطح دسترسی", Icon = "Shield", IconColor = "#2563EB", Route = "/admin/access-level", Order = 4, ParentId = settingsMenu.Id },
        };
        foreach (var rm in requiredMenus)
        {
            if (!await db.MenuItems.AnyAsync(x => x.Key == rm.Key, cancellationToken))
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Key = rm.Key, Title = rm.Title, Icon = rm.Icon, IconColor = rm.IconColor, Route = rm.Route, Order = rm.Order, ParentId = rm.ParentId });
        }
        var usersMenuForChild = await db.MenuItems.FirstAsync(x => x.Key == "users", cancellationToken);
        var respondersMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "responders", cancellationToken);
        if (respondersMenu is null)
        {
            respondersMenu = new MenuItem { Id = Guid.NewGuid(), Key = "responders", Title = "مدیریت پاسخگو", Icon = "Phone", IconColor = "#0EA5E9", Route = null, Order = 3 };
            db.MenuItems.Add(respondersMenu);
        }
        else
        {
            respondersMenu.Route = null;
            respondersMenu.Icon = "Phone";
            respondersMenu.IconColor = "#0EA5E9";
        }
        var extraMenus = new[]
        {
            new MenuItem { Key = "forms.list", Title = "لیست فرم‌ها", Icon = "LayoutTemplate", IconColor = "#10B981", Route = "/admin/forms", Order = 1, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.rules", Title = "شرط فرم", Icon = "GitBranch", IconColor = "#2563EB", Route = "/admin/forms/rules", Order = 2, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.access", Title = "ارجاع فرم", Icon = "FileText", IconColor = "#0EA5E9", Route = "/admin/forms/access", Order = 3, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.workflows.list", Title = "گردش‌های ذخیره‌شده", Icon = "GitBranch", IconColor = "#7C3AED", Route = "/admin/forms/workflows/list", Order = 4, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.workflows", Title = "ایجاد گردش", Icon = "GitBranch", IconColor = "#8B5CF6", Route = "/admin/forms/workflows", Order = 5, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.workflow-runs", Title = "لیست گردش کار", Icon = "GitBranch", IconColor = "#0D9488", Route = "/admin/forms/workflow-runs", Order = 7, ParentId = formsMenu.Id },
            new MenuItem { Key = "forms.archive", Title = "بایگانی", Icon = "Archive", IconColor = "#64748B", Route = "/admin/forms/archive", Order = 8, ParentId = formsMenu.Id },
            new MenuItem { Key = "users.list", Title = "لیست کاربران", Icon = "Users", IconColor = "#0EA5E9", Route = "/admin/users", Order = 1, ParentId = usersMenuForChild.Id },
            new MenuItem { Key = "users.create", Title = "ایجاد کاربر", Icon = "User", IconColor = "#10B981", Route = "/admin/users/create", Order = 2, ParentId = usersMenuForChild.Id },
            new MenuItem { Key = "responders.list", Title = "لیست پاسخگو", Icon = "Phone", IconColor = "#0EA5E9", Route = "/admin/responders", Order = 1, ParentId = respondersMenu.Id },
            new MenuItem { Key = "responders.create", Title = "ایجاد پاسخگو", Icon = "User", IconColor = "#10B981", Route = "/admin/responders/create", Order = 2, ParentId = respondersMenu.Id },
            new MenuItem { Key = "responders.groups", Title = "گروه‌بندی", Icon = "Phone", IconColor = "#2563EB", Route = "/admin/responders/groups", Order = 3, ParentId = respondersMenu.Id },
            new MenuItem { Key = "responders.send", Title = "ارسال فرم", Icon = "Send", IconColor = "#4F46E5", Route = "/admin/responders/send", Order = 4, ParentId = respondersMenu.Id },
            new MenuItem { Key = "responders.userforms", Title = "فرم کاربران", Icon = "FileText", IconColor = "#0EA5E9", Route = "/admin/responders/user-forms", Order = 5, ParentId = respondersMenu.Id },
            new MenuItem { Key = "users.groups", Title = "گروه‌بندی", Icon = "Users", IconColor = "#2563EB", Route = "/admin/users/groups", Order = 3, ParentId = usersMenuForChild.Id },
            new MenuItem { Key = "settings.users", Title = "تنظیمات کاربران", Icon = "Settings2", IconColor = "#0EA5E9", Route = "/admin/settings/users", Order = 4, ParentId = usersMenuForChild.Id },
            new MenuItem { Key = "settings.responders", Title = "تنظیمات پاسخگو", Icon = "Settings2", IconColor = "#0EA5E9", Route = "/admin/settings/responders", Order = 6, ParentId = respondersMenu.Id },
            new MenuItem { Key = "profile", Title = "پروفایل", Icon = "User", IconColor = "#0EA5E9", Route = "/admin/profile", Order = 5, ParentId = null },
            new MenuItem { Key = "messages", Title = "صندوق پیام", Icon = "Mail", IconColor = "#A855F7", Route = "/admin/messages", Order = 6, ParentId = null },
        };
        foreach (var rm in extraMenus)
        {
            var existing = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == rm.Key, cancellationToken);
            if (existing is null)
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Key = rm.Key, Title = rm.Title, Icon = rm.Icon, IconColor = rm.IconColor, Route = rm.Route, Order = rm.Order, ParentId = rm.ParentId });
            else if (rm.Key is "forms.workflows" or "forms.workflows.list" or "forms.workflow-runs" or "forms.archive"
                or "settings.users" or "settings.responders")
            {
                existing.Title = rm.Title;
                existing.Route = rm.Route;
                existing.Order = rm.Order;
                existing.Icon = rm.Icon;
                existing.IconColor = rm.IconColor;
                existing.ParentId = rm.ParentId;
            }
        }

        await ReparentUserAndResponderSettingsMenusAsync(db, usersMenuForChild.Id, respondersMenu.Id, cancellationToken);

        var approvalsMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "approvals", cancellationToken);
        if (approvalsMenu is not null)
        {
            var approvalRoleMenus = await db.RoleMenus.Where(rm => rm.MenuId == approvalsMenu.Id).ToListAsync(cancellationToken);
            if (approvalRoleMenus.Count > 0)
                db.RoleMenus.RemoveRange(approvalRoleMenus);
            db.MenuItems.Remove(approvalsMenu);
        }

        var contractsMenu = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "contracts", cancellationToken);
        if (contractsMenu is null)
        {
            contractsMenu = new MenuItem { Id = Guid.NewGuid(), Key = "contracts", Title = "گردش قرارداد", Icon = "FileText", IconColor = "#4F46E5", Route = null, Order = 3 };
            db.MenuItems.Add(contractsMenu);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            contractsMenu.Title = "گردش قرارداد";
            contractsMenu.Icon = "FileText";
            contractsMenu.IconColor = "#4F46E5";
            contractsMenu.Route = null;
            contractsMenu.Order = 3;
        }
        contractsMenu = await db.MenuItems.FirstAsync(x => x.Key == "contracts", cancellationToken);
        var contractChildMenus = new[]
        {
            new MenuItem { Key = "contracts.list", Title = "لیست قراردادها", Icon = "FileText", IconColor = "#4F46E5", Route = "/admin/contracts", Order = 1, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.create", Title = "ایجاد قرارداد", Icon = "Plus", IconColor = "#4F46E5", Route = "/admin/contracts?create=1", Order = 2, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.workflows.list", Title = "گردش‌های ذخیره‌شده", Icon = "GitBranch", IconColor = "#7C3AED", Route = "/admin/contracts/workflows/list", Order = 3, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.workflows", Title = "ایجاد گردش", Icon = "GitBranch", IconColor = "#8B5CF6", Route = "/admin/contracts/workflows", Order = 4, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.templates", Title = "قالب‌های قرارداد", Icon = "FileType", IconColor = "#0D9488", Route = "/admin/contracts/templates", Order = 5, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.settings", Title = "تنظیمات قرارداد", Icon = "Settings2", IconColor = "#6366F1", Route = "/admin/contracts/settings", Order = 6, ParentId = contractsMenu.Id },
            new MenuItem { Key = "actions.list", Title = "اقدامات", Icon = "ClipboardList", IconColor = "#D97706", Route = "/admin/actions", Order = 7, ParentId = contractsMenu.Id },
            new MenuItem { Key = "contracts.archive", Title = "بایگانی", Icon = "Archive", IconColor = "#64748B", Route = "/admin/contracts/archive", Order = 8, ParentId = contractsMenu.Id },
        };
        foreach (var cm in contractChildMenus)
        {
            var existing = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == cm.Key, cancellationToken);
            if (existing is null)
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Key = cm.Key, Title = cm.Title, Icon = cm.Icon, IconColor = cm.IconColor, Route = cm.Route, Order = cm.Order, ParentId = cm.ParentId });
            else
            {
                existing.Title = cm.Title;
                existing.Route = cm.Route;
                existing.Order = cm.Order;
                existing.Icon = cm.Icon;
                existing.IconColor = cm.IconColor;
                existing.ParentId = cm.ParentId;
            }
        }

        await ReparentActionsMenuUnderContractsAsync(db, contractsMenu.Id, cancellationToken);

        // ذخیره منوها قبل از جداول قرارداد — اگر migration هنوز اعمال نشده باشد، منو همچنان ثبت می‌شود
        await db.SaveChangesAsync(cancellationToken);

        if (await TableExistsAsync(db, "ContractSettings", cancellationToken))
        {
            if (!await db.ContractSettings.AnyAsync(cancellationToken))
                db.ContractSettings.Add(new ContractSettings { Id = 1, ApprovalEnabled = false });
        }

        if (await TableExistsAsync(db, "ContractTypes", cancellationToken)
            && !await db.ContractTypes.AnyAsync(cancellationToken))
        {
            db.ContractTypes.Add(new ContractType { Id = Guid.NewGuid(), Name = "قرارداد دائم", SortOrder = 1, IsActive = true });
            db.ContractTypes.Add(new ContractType { Id = Guid.NewGuid(), Name = "قرارداد موقت", SortOrder = 2, IsActive = true });
        }

        if (!await db.SecuritySettings.AnyAsync(cancellationToken))
            db.SecuritySettings.Add(new SecuritySettings());
        if (!await db.SmsSettings.AnyAsync(cancellationToken))
            db.SmsSettings.Add(new SmsSettings { UserCreateSmsEnabled = true });
        else
        {
            var sms = await db.SmsSettings.FirstAsync(cancellationToken);
            // ensure new field populated for old rows
            if (!sms.UserCreateSmsEnabled) { /* keep admin choice if false */ }
        }

        await db.SaveChangesAsync(cancellationToken);

        // RolePermissions/RoleMenus
        var admin = await roleManager.FindByNameAsync("Admin");
        var expert = await roleManager.FindByNameAsync("Expert");
        if (admin is null || expert is null) return;

        var perms = await db.Permissions.ToDictionaryAsync(x => x.Name, x => x.Id, cancellationToken);
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);

        var adminPerms = permissionNames;
        var expertPerms = new[] { "menus.view", "profile.update", "messages.read", "forms.read", "forms.add", "forms.update", "workflow-runs.read", "workflow-runs.update", "actions.read", "actions.update", "responders.send", "contracts.read", "contracts.add", "contracts.update" };
        foreach (var p in adminPerms)
            if (perms.TryGetValue(p, out var pid) && !await db.RolePermissions.AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == pid, cancellationToken))
                db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = pid });
        foreach (var p in expertPerms)
            if (perms.TryGetValue(p, out var pid) && !await db.RolePermissions.AnyAsync(x => x.RoleId == expert.Id && x.PermissionId == pid, cancellationToken))
                db.RolePermissions.Add(new RolePermission { RoleId = expert.Id, PermissionId = pid });

        var adminMenuKeys = new[] { "dashboard", "forms", "forms.list", "forms.rules", "forms.access", "forms.workflows.list", "forms.workflows", "forms.workflow-runs", "forms.archive", "contracts", "contracts.list", "contracts.create", "contracts.workflows.list", "contracts.workflows", "contracts.templates", "contracts.settings", "contracts.archive", "actions.list", "users", "users.list", "users.create", "users.groups", "responders", "responders.list", "responders.create", "responders.groups", "responders.send", "responders.userforms", "settings", "settings.site", "settings.sms", "settings.security", "settings.access", "settings.responders", "settings.users", "profile", "messages" };
        var expertMenuKeys = new[] { "dashboard", "forms", "forms.list", "forms.rules", "forms.workflows.list", "forms.workflows", "forms.workflow-runs", "contracts", "contracts.list", "contracts.create", "actions.list", "users", "users.list", "responders", "responders.list", "responders.send", "responders.userforms", "profile", "messages" };
        foreach (var k in adminMenuKeys)
            if (menus.TryGetValue(k, out var mid) && !await db.RoleMenus.AnyAsync(x => x.RoleId == admin.Id && x.MenuId == mid, cancellationToken))
                db.RoleMenus.Add(new RoleMenu { RoleId = admin.Id, MenuId = mid });
        foreach (var k in expertMenuKeys)
            if (menus.TryGetValue(k, out var mid) && !await db.RoleMenus.AnyAsync(x => x.RoleId == expert.Id && x.MenuId == mid, cancellationToken))
                db.RoleMenus.Add(new RoleMenu { RoleId = expert.Id, MenuId = mid });

        await SyncContractMenusForRolesWithPermissionAsync(db, cancellationToken);
        await SyncActionsMenusForRolesWithPermissionAsync(db, cancellationToken);
        await SyncWorkflowRunsMenusForRolesWithPermissionAsync(db, cancellationToken);
        await SyncFormsArchiveMenusForRolesWithPermissionAsync(db, cancellationToken);
        await SyncContractsArchiveMenusForRolesWithPermissionAsync(db, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>«تنظیمات کاربران» و «تنظیمات پاسخگو» را از تنظیمات سایت به منوی مدیریت کاربران/پاسخگو منتقل می‌کند.</summary>
    static async Task ReparentUserAndResponderSettingsMenusAsync(
        AppDbContext db,
        Guid usersMenuId,
        Guid respondersMenuId,
        CancellationToken cancellationToken)
    {
        var usersSettings = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "settings.users", cancellationToken);
        if (usersSettings is null)
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(),
                Key = "settings.users",
                Title = "تنظیمات کاربران",
                Icon = "Settings2",
                IconColor = "#0EA5E9",
                Route = "/admin/settings/users",
                Order = 4,
                ParentId = usersMenuId,
            });
        }
        else
        {
            usersSettings.ParentId = usersMenuId;
            usersSettings.Order = 4;
            usersSettings.Title = "تنظیمات کاربران";
            usersSettings.Route = "/admin/settings/users";
            usersSettings.Icon = "Settings2";
            usersSettings.IconColor = "#0EA5E9";
        }

        var respondersSettings = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "settings.responders", cancellationToken);
        if (respondersSettings is null)
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(),
                Key = "settings.responders",
                Title = "تنظیمات پاسخگو",
                Icon = "Settings2",
                IconColor = "#0EA5E9",
                Route = "/admin/settings/responders",
                Order = 6,
                ParentId = respondersMenuId,
            });
        }
        else
        {
            respondersSettings.ParentId = respondersMenuId;
            respondersSettings.Order = 6;
            respondersSettings.Title = "تنظیمات پاسخگو";
            respondersSettings.Route = "/admin/settings/responders";
            respondersSettings.Icon = "Settings2";
            respondersSettings.IconColor = "#0EA5E9";
        }
    }

    /// <summary>منوی اقدامات را زیر «گردش قرارداد» قرار می‌دهد و منوی ریشهٔ قدیمی actions را حذف می‌کند.</summary>
    static async Task ReparentActionsMenuUnderContractsAsync(
        AppDbContext db,
        Guid contractsMenuId,
        CancellationToken cancellationToken)
    {
        var actionsList = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "actions.list", cancellationToken);
        if (actionsList is null)
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(),
                Key = "actions.list",
                Title = "اقدامات",
                Icon = "ClipboardList",
                IconColor = "#D97706",
                Route = "/admin/actions",
                Order = 7,
                ParentId = contractsMenuId,
            });
        }
        else
        {
            actionsList.ParentId = contractsMenuId;
            actionsList.Order = 7;
            actionsList.Title = "اقدامات";
            actionsList.Route = "/admin/actions";
            actionsList.Icon = "ClipboardList";
            actionsList.IconColor = "#D97706";
        }

        var actionsRoot = await db.MenuItems.FirstOrDefaultAsync(x => x.Key == "actions", cancellationToken);
        if (actionsRoot is null) return;

        var legacyChildren = await db.MenuItems
            .Where(x => x.ParentId == actionsRoot.Id && x.Key != "actions.list")
            .ToListAsync(cancellationToken);
        foreach (var child in legacyChildren)
            child.ParentId = contractsMenuId;

        var roleMenusOnRoot = await db.RoleMenus.Where(rm => rm.MenuId == actionsRoot.Id).ToListAsync(cancellationToken);
        if (roleMenusOnRoot.Count > 0)
            db.RoleMenus.RemoveRange(roleMenusOnRoot);

        db.MenuItems.Remove(actionsRoot);
    }

    /// <summary>منوی «اقدامات» را به نقش‌های دارای actions.read وصل می‌کند (زیر گردش قرارداد).</summary>
    public static async Task SyncActionsMenusForRolesWithPermissionAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("contracts") || !menus.ContainsKey("actions.list")) return;

        var permIds = await db.Permissions
            .Where(p => p.Name == "actions.read" || p.Name == "actions.read.all" || p.Name == "actions.update")
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (permIds.Count == 0) return;

        var targetRoleIds = await db.RolePermissions
            .Where(rp => permIds.Contains(rp.PermissionId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var entry in db.ChangeTracker.Entries<RoleMenu>())
        {
            if (entry.State is EntityState.Added or EntityState.Unchanged or EntityState.Modified)
                linked.Add((entry.Entity.RoleId, entry.Entity.MenuId));
        }

        foreach (var roleId in targetRoleIds)
        {
            foreach (var key in new[] { "contracts", "contracts.list", "actions.list" })
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    /// <summary>منوی «بایگانی فرم» را به نقش‌های دارای forms.archive.read وصل می‌کند.</summary>
    public static async Task SyncFormsArchiveMenusForRolesWithPermissionAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("forms") || !menus.ContainsKey("forms.archive")) return;

        var permIds = await db.Permissions
            .Where(p => p.Name == "forms.archive.read" || p.Name == "forms.archive.read.all")
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (permIds.Count == 0) return;

        var targetRoleIds = await db.RolePermissions
            .Where(rp => permIds.Contains(rp.PermissionId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var roleId in targetRoleIds)
        {
            foreach (var key in new[] { "forms", "forms.archive" })
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    /// <summary>منوی «بایگانی قرارداد» را به نقش‌های دارای contracts.archive.read وصل می‌کند.</summary>
    public static async Task SyncContractsArchiveMenusForRolesWithPermissionAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("contracts") || !menus.ContainsKey("contracts.archive")) return;

        var permIds = await db.Permissions
            .Where(p => p.Name == "contracts.archive.read" || p.Name == "contracts.archive.read.all")
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (permIds.Count == 0) return;

        var targetRoleIds = await db.RolePermissions
            .Where(rp => permIds.Contains(rp.PermissionId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var roleId in targetRoleIds)
        {
            foreach (var key in new[] { "contracts", "contracts.archive" })
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    /// <summary>منوی «لیست گردش کار» را به نقش‌های دارای workflow-runs.read وصل می‌کند.</summary>
    public static async Task SyncWorkflowRunsMenusForRolesWithPermissionAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("forms") || !menus.ContainsKey("forms.workflow-runs")) return;

        var permIds = await db.Permissions
            .Where(p => p.Name == "workflow-runs.read" || p.Name == "workflow-runs.update")
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (permIds.Count == 0) return;

        var targetRoleIds = await db.RolePermissions
            .Where(rp => permIds.Contains(rp.PermissionId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var roleId in targetRoleIds)
        {
            foreach (var key in new[] { "forms", "forms.workflow-runs" })
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    /// <summary>
    /// منوی «تأییدیه‌ها» را زیر «فرم ساز» به هر نقشی که approvals.read یا approvals.update دارد وصل می‌کند.
    /// </summary>
    public static async Task SyncApprovalsMenusForRolesWithPermissionAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("forms") || !menus.ContainsKey("approvals")) return;

        var approvalPermIds = await db.Permissions
            .Where(p => p.Name == "approvals.read" || p.Name == "approvals.update")
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (approvalPermIds.Count == 0) return;

        var targetRoleIds = await db.RolePermissions
            .Where(rp => approvalPermIds.Contains(rp.PermissionId))
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var entry in db.ChangeTracker.Entries<RoleMenu>())
        {
            if (entry.State is EntityState.Added or EntityState.Unchanged or EntityState.Modified)
                linked.Add((entry.Entity.RoleId, entry.Entity.MenuId));
        }

        foreach (var roleId in targetRoleIds)
        {
            foreach (var key in new[] { "forms", "approvals" })
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    static async Task<bool> TableExistsAsync(AppDbContext db, string tableName, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT CASE WHEN EXISTS (" +
            "SELECT 1 FROM INFORMATION_SCHEMA.TABLES " +
            "WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME = @table) THEN 1 ELSE 0 END";
        var p = cmd.CreateParameter();
        p.ParameterName = "@table";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is int i && i == 1;
    }

    /// <summary>
    /// منوی «گردش قرارداد» را به هر نقشی که پرمیژن contracts.read یا contracts.settings.read دارد وصل می‌کند.
    /// </summary>
    public static async Task SyncContractMenusForRolesWithPermissionAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var menus = await db.MenuItems.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        if (!menus.ContainsKey("contracts") || !menus.ContainsKey("contracts.list")) return;

        var contractPermNames = new[] { "contracts.read", "contracts.add", "contracts.settings.read", "contracts.settings.update" };
        var contractPermIds = await db.Permissions
            .Where(p => contractPermNames.Contains(p.Name))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        if (contractPermIds.Count == 0) return;

        var readPermId = contractPermIds.FirstOrDefault(p => p.Name == "contracts.read")?.Id;
        var addPermId = contractPermIds.FirstOrDefault(p => p.Name == "contracts.add")?.Id;
        var settingsPermIds = contractPermIds
            .Where(p => p.Name is "contracts.settings.read" or "contracts.settings.update")
            .Select(p => p.Id)
            .ToHashSet();

        var roleIdsWithRead = readPermId is null
            ? []
            : await db.RolePermissions.Where(rp => rp.PermissionId == readPermId).Select(rp => rp.RoleId).Distinct().ToListAsync(cancellationToken);

        var roleIdsWithAdd = addPermId is null
            ? []
            : await db.RolePermissions.Where(rp => rp.PermissionId == addPermId).Select(rp => rp.RoleId).Distinct().ToListAsync(cancellationToken);

        var roleIdsWithSettings = settingsPermIds.Count == 0
            ? []
            : await db.RolePermissions.Where(rp => settingsPermIds.Contains(rp.PermissionId)).Select(rp => rp.RoleId).Distinct().ToListAsync(cancellationToken);

        var targetRoleIds = roleIdsWithRead.Union(roleIdsWithAdd).Union(roleIdsWithSettings).Distinct().ToList();
        if (targetRoleIds.Count == 0) return;

        var linked = (await db.RoleMenus
                .AsNoTracking()
                .Where(rm => targetRoleIds.Contains(rm.RoleId))
                .Select(rm => new { rm.RoleId, rm.MenuId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.RoleId, x.MenuId))
            .ToHashSet();

        foreach (var entry in db.ChangeTracker.Entries<RoleMenu>())
        {
            if (entry.State is EntityState.Added or EntityState.Unchanged or EntityState.Modified)
                linked.Add((entry.Entity.RoleId, entry.Entity.MenuId));
        }

        foreach (var roleId in targetRoleIds)
        {
            var keys = new List<string> { "contracts", "contracts.list" };
            if (roleIdsWithAdd.Contains(roleId) && menus.ContainsKey("contracts.create"))
                keys.Add("contracts.create");
            if (roleIdsWithSettings.Contains(roleId) && menus.ContainsKey("contracts.workflows.list"))
                keys.Add("contracts.workflows.list");
            if (roleIdsWithSettings.Contains(roleId) && menus.ContainsKey("contracts.workflows"))
                keys.Add("contracts.workflows");
            if (roleIdsWithSettings.Contains(roleId) && menus.ContainsKey("contracts.templates"))
                keys.Add("contracts.templates");
            if (roleIdsWithSettings.Contains(roleId) && menus.ContainsKey("contracts.settings"))
                keys.Add("contracts.settings");

            foreach (var key in keys)
            {
                if (!menus.TryGetValue(key, out var menuId)) continue;
                if (linked.Contains((roleId, menuId))) continue;
                db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
                linked.Add((roleId, menuId));
            }
        }
    }

    /// <summary>
    /// کاربر Admin و چند کاربر Expert پیش‌فرض را به‌صورت idempotent ایجاد/به‌روزرسانی می‌کند.
    /// </summary>
    public static async Task SeedAdminUserAsync(AppDbContext db, UserManager<AppUser> userManager)
    {
        var seededUserIds = new HashSet<Guid>();
        var admin = await userManager.Users.FirstOrDefaultAsync(x =>
            x.PhoneNumber == "09120000000" || x.NationalCode == "0012345678");

        if (admin is null)
        {
            admin = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = "09120000000",
                PhoneNumber = "09132251359",
                FirstName = "مدیر",
                LastName = "سیستم",
                NationalCode = "0012345678",
                IsActive = true,
                PhoneNumberConfirmed = true
            };

            var create = await userManager.CreateAsync(admin, "Admin@123456");
            if (!create.Succeeded) return;
        }
        else
        {
            admin.UserName = "09120000000";
            admin.PhoneNumber = "09132251359";
            admin.FirstName = string.IsNullOrWhiteSpace(admin.FirstName) ? "مدیر" : admin.FirstName;
            admin.LastName = string.IsNullOrWhiteSpace(admin.LastName) ? "سیستم" : admin.LastName;
            admin.NationalCode = "0012345678";
            admin.IsActive = true;
            admin.PhoneNumberConfirmed = true;
            await userManager.UpdateAsync(admin);
        }

        var roles = await userManager.GetRolesAsync(admin);
        if (!roles.Contains("Admin"))
            await userManager.AddToRoleAsync(admin, "Admin");
        seededUserIds.Add(admin.Id);

        if (!await db.InboxMessages.AnyAsync(x => x.UserId == admin.Id))
        {
            db.InboxMessages.Add(new InboxMessage
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                Title = "خوش آمدید",
                Body = "پنل مدیریت شما آماده استفاده است.",
                IsRead = false
            });
            await db.SaveChangesAsync();
        }

        var expertSeeds = new[]
        {
            new { UserName = "09121000001", Phone = "09121000001", NationalCode = "0010000001", FirstName = "کارشناس", LastName = "اول" },
            new { UserName = "09121000002", Phone = "09121000002", NationalCode = "0010000002", FirstName = "کارشناس", LastName = "دوم" },
            new { UserName = "09121000003", Phone = "09121000003", NationalCode = "0010000003", FirstName = "کارشناس", LastName = "سوم" },
            new { UserName = "09121000004", Phone = "09121000004", NationalCode = "0010000004", FirstName = "کارشناس", LastName = "چهارم" },
        };

        foreach (var s in expertSeeds)
        {
            var expert = await userManager.Users.FirstOrDefaultAsync(x =>
                x.PhoneNumber == s.Phone || x.NationalCode == s.NationalCode);

            if (expert is null)
            {
                expert = new AppUser
                {
                    Id = Guid.NewGuid(),
                    UserName = s.UserName,
                    PhoneNumber = s.Phone,
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    NationalCode = s.NationalCode,
                    IsActive = true,
                    PhoneNumberConfirmed = true
                };

                var create = await userManager.CreateAsync(expert, "Expert@123456");
                if (!create.Succeeded) continue;
            }
            else
            {
                expert.UserName = s.UserName;
                expert.PhoneNumber = s.Phone;
                expert.FirstName = string.IsNullOrWhiteSpace(expert.FirstName) ? s.FirstName : expert.FirstName;
                expert.LastName = string.IsNullOrWhiteSpace(expert.LastName) ? s.LastName : expert.LastName;
                expert.NationalCode = s.NationalCode;
                expert.IsActive = true;
                expert.PhoneNumberConfirmed = true;
                await userManager.UpdateAsync(expert);
            }

            var expertRoles = await userManager.GetRolesAsync(expert);
            if (!expertRoles.Contains("Expert"))
                await userManager.AddToRoleAsync(expert, "Expert");

            seededUserIds.Add(expert.Id);
        }

        const string seededUsersGroupName = "کاربران سید";
        var seededUsersGroup = await db.UserGroups.FirstOrDefaultAsync(x => x.Name == seededUsersGroupName);
        if (seededUsersGroup is null)
        {
            seededUsersGroup = new UserGroup
            {
                Id = Guid.NewGuid(),
                Name = seededUsersGroupName,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.UserGroups.Add(seededUsersGroup);
            await db.SaveChangesAsync();
        }

        foreach (var userId in seededUserIds)
        {
            var isMember = await db.UserGroupMembers.AnyAsync(x => x.UserId == userId && x.GroupId == seededUsersGroup.Id);
            if (!isMember)
                db.UserGroupMembers.Add(new UserGroupMember { UserId = userId, GroupId = seededUsersGroup.Id });
        }

        await db.SaveChangesAsync();
    }
}
