using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
public class AdminUsersController(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ISmsSender smsSender,
    Infrastructure.Persistence.AppDbContext db,
    IFrontendUrlResolver frontendUrls,
    UserSignatureStorageService signatureStorage,
    IWebHostEnvironment env) : ControllerBase
{
    private Guid? CurrentUserGuid
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : null;
        }
    }
    private bool CanReadAllUsers => User.HasClaim("permission", "users.read.all");

    [HttpPost]
    [Authorize(Policy = "users.add")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken cancellationToken)
    {
        var firstName = dto.FirstName.Trim();
        var lastName = dto.LastName.Trim();
        var mobileNumber = dto.MobileNumber.Trim();
        var nationalCode = dto.NationalCode.Trim();

        if (firstName.Length < 2 || lastName.Length < 2)
            return BadRequest(new { message = "نام و نام خانوادگی باید حداقل 2 کاراکتر باشند" });

        if (await userManager.Users.AnyAsync(x => x.PhoneNumber == mobileNumber, cancellationToken))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        if (await userManager.Users.AnyAsync(x => x.NationalCode == nationalCode, cancellationToken))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var validGroupIds = await db.UserGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validGroupIds.Count != groupIds.Count)
            return BadRequest(new { message = "یک یا چند گروه انتخاب‌شده معتبر نیستند" });

        Guid? positionId = null;
        if (dto.UserPositionId is Guid pid && pid != Guid.Empty)
        {
            if (!await db.UserPositions.AnyAsync(x => x.Id == pid && x.IsActive, cancellationToken))
                return BadRequest(new { message = "سمت انتخاب‌شده معتبر نیست" });
            positionId = pid;
        }

        var generatedPassword = PasswordGenerator.Generate();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = mobileNumber,
            PhoneNumber = mobileNumber,
            FirstName = firstName,
            LastName = lastName,
            NationalCode = nationalCode,
            UserPositionId = positionId,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            PhoneNumberConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, generatedPassword);
        if (!createResult.Succeeded)
            return BadRequest(new
            {
                message = "ایجاد کاربر ناموفق بود",
                errors = createResult.Errors.Select(x => x.Description).ToList()
            });

        foreach (var gid in validGroupIds)
            db.UserGroupMembers.Add(new UserGroupMember { UserId = user.Id, GroupId = gid });
        await db.SaveChangesAsync(cancellationToken);

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken) ?? new SmsSettings();

        bool smsSent = false;
        string? smsFailReason = null;

        if (smsSettings.UserCreateSmsEnabled)
        {
            var nowTehran = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"));

            var dateStr  = nowTehran.ToString("yyyy/MM/dd");
            var timeStr  = nowTehran.ToString("HH:mm");
            var baseUrl = await frontendUrls.ResolveAdminBaseUrlAsync(cancellationToken);
            var loginUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/login" : $"{baseUrl}/login";

            var smsText =
                $"کارشناس محترم {firstName} {lastName}،\n" +
                $"کاربری شما در تاریخ {dateStr} ساعت {timeStr} ساخته شد.\n" +
                $"جهت استفاده از پنل به لینک زیر مراجعه نمایید:\n{loginUrl}";

            smsSent = await smsSender.SendSmsAsync(new SmsRequest(mobileNumber, smsText), cancellationToken);

            if (!smsSent)
                smsFailReason = "ارسال پیامک با خطا مواجه شد";
        }

        return Ok(new
        {
            id = user.Id,
            message = !smsSettings.UserCreateSmsEnabled
                ? "کاربر با موفقیت ساخته شد"
                : smsSent
                    ? "کاربر ساخته شد و پیامک خوش‌آمدگویی ارسال شد"
                    : $"کاربر ساخته شد ولی {smsFailReason}",
            smsSent,
            smsEnabled = smsSettings.UserCreateSmsEnabled
        });
    }

    [HttpGet]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var query = userManager.Users.Where(x => !x.IsSoftDeleted).AsQueryable();
        if (!CanReadAllUsers)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }

        // فیلتر جستجو
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x =>
                (x.FirstName + " " + x.LastName).Contains(q) ||
                x.PhoneNumber!.Contains(q) ||
                x.NationalCode.Contains(q));
        }

        // فیلتر وضعیت
        if (status == "active")   query = query.Where(x => x.IsActive);
        if (status == "inactive") query = query.Where(x => !x.IsActive);

        // فیلتر گروه
        if (groupId is Guid gid && gid != Guid.Empty)
        {
            var memberUserIds = db.UserGroupMembers
                .Where(x => x.GroupId == gid)
                .Select(x => x.UserId);
            query = query.Where(x => memberUserIds.Contains(x.Id));
        }

        // مرتب‌سازی
        query = sortBy switch
        {
            "created_asc"  => query.OrderBy(x => x.CreatedAtUtc),
            "name_asc"     => query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName),
            "name_desc"    => query.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName),
            _              => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var total = await query.CountAsync(cancellationToken);

        var usersRaw = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                MobileNumber = x.PhoneNumber,
                x.NationalCode,
                x.CreatedAtUtc,
                x.IsActive,
                x.UserPositionId,
                x.SignatureImagePath
            })
            .ToListAsync(cancellationToken);

        // بارگذاری role‌ها به صورت batch
        var userIds = usersRaw.Select(x => x.Id).ToList();
        var positionIds = usersRaw.Where(x => x.UserPositionId.HasValue).Select(x => x.UserPositionId!.Value).Distinct().ToList();
        var positionsById = await db.UserPositions
            .Where(x => positionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var userRoles = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            .Where(x => userIds.Contains(x.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var rolesByUser = userRoles.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var userGroups = await db.UserGroupMembers
            .Where(x => userIds.Contains(x.UserId))
            .Join(db.UserGroups, ug => ug.GroupId, g => g.Id, (ug, g) => new UserGroupOptionDto(ug.UserId, g.Id, g.Name))
            .ToListAsync(cancellationToken);
        var groupsByUser = userGroups
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => (object)new { x.Id, x.Name }).ToList());

        var items = usersRaw.Select(u =>
        {
            var roles = rolesByUser.TryGetValue(u.Id, out var r) ? r : new List<string>();
            var groupNames = groupsByUser.TryGetValue(u.Id, out var g) ? g : new List<object>();
            return new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.MobileNumber,
                u.NationalCode,
                u.CreatedAtUtc,
                u.IsActive,
                u.UserPositionId,
                UserPositionName = u.UserPositionId.HasValue && positionsById.TryGetValue(u.UserPositionId.Value, out var pn) ? pn : null,
                HasSignature = !string.IsNullOrWhiteSpace(u.SignatureImagePath),
                SignaturePath = u.SignatureImagePath,
                RoleName = roles.FirstOrDefault() ?? "",
                RoleNames = roles,
                Groups = groupNames
            };
        }).ToList();

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("roles-options")]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> RolesOptions(CancellationToken cancellationToken)
    {
        var roles = await dbRoles(cancellationToken);
        return Ok(roles);
    }

    [HttpGet("workflow-users")]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> WorkflowUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Where(x => !x.IsSoftDeleted && x.IsActive)
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                Name = (x.FirstName + " " + x.LastName).Trim(),
                Email = x.Email ?? (x.PhoneNumber ?? ""),
                x.AvatarUrl,
                PositionName = x.UserPosition != null ? x.UserPosition.Name : null
            })
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.FirstName = dto.FirstName.Trim();
        user.LastName = dto.LastName.Trim();
        user.PhoneNumber = dto.MobileNumber.Trim();
        user.UserName = dto.MobileNumber.Trim();
        user.NationalCode = dto.NationalCode.Trim();

        if (dto.UserPositionId is Guid pid && pid != Guid.Empty)
        {
            if (!await db.UserPositions.AnyAsync(x => x.Id == pid && x.IsActive))
                return BadRequest(new { message = "سمت انتخاب‌شده معتبر نیست" });
            user.UserPositionId = pid;
        }
        else
            user.UserPositionId = null;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors);

        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        var oldMembers = await db.UserGroupMembers.Where(x => x.UserId == id).ToListAsync();
        db.UserGroupMembers.RemoveRange(oldMembers);
        if (groupIds.Count > 0)
        {
            var validGroups = await db.UserGroups.Where(x => groupIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            foreach (var gid in validGroups)
                db.UserGroupMembers.Add(new UserGroupMember { UserId = id, GroupId = gid });
        }
        await db.SaveChangesAsync();
        return Ok(new { message = "مشخصات کاربر بروزرسانی شد" });
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateUserRoleDto dto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsSoftDeleted) return NotFound();

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
            await userManager.RemoveFromRolesAsync(user, currentRoles);

        var add = await userManager.AddToRoleAsync(user, dto.RoleName);
        if (!add.Succeeded) return BadRequest(add.Errors);

        return Ok(new { message = "نقش کاربر بروزرسانی شد" });
    }

    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> UpdateRoles(Guid id, [FromBody] SetUserRolesDto dto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var roleNames = dto.RoleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roleNames.Count == 0) return BadRequest(new { message = "حداقل یک نقش باید انتخاب شود" });

        var existingRoles = await dbRoles(CancellationToken.None);
        var existingRoleNames = existingRoles.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roleNames.Any(x => !existingRoleNames.Contains(x)))
            return BadRequest(new { message = "برخی نقش‌ها معتبر نیستند" });

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
            await userManager.RemoveFromRolesAsync(user, currentRoles);

        var add = await userManager.AddToRolesAsync(user, roleNames);
        if (!add.Succeeded) return BadRequest(add.Errors);

        return Ok(new { message = "نقش‌های کاربر بروزرسانی شد" });
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateUserStatusDto dto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        user.IsActive = dto.IsActive;
        await userManager.UpdateAsync(user);
        return Ok(new { message = "وضعیت کاربر بروزرسانی شد" });
    }

    [HttpGet("{id:guid}/signature")]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> GetSignature(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.SignatureImagePath)) return NotFound();
        var full = UserSignatureStorageService.ResolveFullPath(env, user.SignatureImagePath);
        if (!System.IO.File.Exists(full)) return NotFound();
        return PhysicalFile(full, "image/png", enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/signature")]
    [Authorize(Policy = "users.update")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadSignature(Guid id, [FromForm] UploadUserSignatureForm form, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsSoftDeleted) return NotFound();

        var file = form.Signature ?? form.File;
        if (file is null && Request.HasFormContentType)
        {
            var f = await Request.ReadFormAsync(ct);
            file = f.Files.GetFile("signature") ?? f.Files.FirstOrDefault();
        }
        if (file is null || file.Length <= 0)
            return BadRequest(new { message = "فایل امضا ارسال نشده است" });

        try
        {
            user.SignatureImagePath = await signatureStorage.SaveAsync(id, file, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return BadRequest(update.Errors);
        return Ok(new { message = "امضای دیجیتال ذخیره شد", signaturePath = user.SignatureImagePath });
    }

    [HttpDelete("{id:guid}/signature")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> DeleteSignature(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsSoftDeleted) return NotFound();
        user.SignatureImagePath = null;
        await userManager.UpdateAsync(user);
        return Ok(new { message = "امضا حذف شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "users.delete")]
    public async Task<IActionResult> SoftDelete(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        user.IsSoftDeleted = true;
        user.IsActive = false;
        await userManager.UpdateAsync(user);
        return Ok(new { message = "کاربر حذف شد" });
    }

    private static class PasswordGenerator
    {
        private const string Upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Lower   = "abcdefghijkmnpqrstuvwxyz";
        private const string Digits  = "23456789";
        private const string Special = "!@#$%^&*";
        private const string All     = Upper + Lower + Digits + Special;

        public static string Generate()
        {
            // تضمین حداقل یک کاراکتر از هر دسته مورد نیاز Identity
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var pool = new char[12];

            pool[0] = Pick(rng, Upper);
            pool[1] = Pick(rng, Lower);
            pool[2] = Pick(rng, Digits);
            pool[3] = Pick(rng, Special);

            for (int i = 4; i < 12; i++)
                pool[i] = Pick(rng, All);

            // جابجایی تصادفی برای امنیت بیشتر
            var bytes = new byte[12];
            rng.GetBytes(bytes);
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = bytes[i] % (i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            return new string(pool);
        }

        private static char Pick(System.Security.Cryptography.RandomNumberGenerator rng, string source)
        {
            var buf = new byte[1];
            rng.GetBytes(buf);
            return source[buf[0] % source.Length];
        }
    }

    private async Task<List<RoleItemDto>> dbRoles(CancellationToken cancellationToken)
    {
        return await roleManager.Roles
            .OrderBy(x => x.Name)
            .Select(x => new RoleItemDto(x.Id, x.Name!, x.DisplayName))
            .ToListAsync(cancellationToken);
    }
}

public record UserGroupOptionDto(Guid UserId, Guid Id, string Name);

public sealed class UploadUserSignatureForm
{
    public IFormFile? Signature { get; set; }
    public IFormFile? File { get; set; }
}

