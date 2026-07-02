using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
public class AdminUsersController(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IInboxMessageService inbox,
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
        var mobileNumber = UserFieldNormalizer.NormalizeMobile(dto.MobileNumber);
        var nationalCode = UserFieldNormalizer.NormalizeNationalCode(dto.NationalCode);

        if (firstName.Length < 2 || lastName.Length < 2)
            return BadRequest(new { message = "نام و نام خانوادگی باید حداقل 2 کاراکتر باشند" });
        if (!UserFieldNormalizer.IsValidMobile(mobileNumber))
            return BadRequest(new { message = "شماره موبایل معتبر نیست (۰۹ و ۹ رقم)" });
        if (!UserFieldNormalizer.IsValidNationalCode(nationalCode))
            return BadRequest(new { message = "کد ملی باید ۱۰ رقم باشد" });

        if (await userManager.Users.AnyAsync(x => x.PhoneNumber == mobileNumber, cancellationToken))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        if (await userManager.Users.AnyAsync(x => x.NationalCode == nationalCode, cancellationToken))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        var personnelCode = dto.PersonnelCode?.Trim();
        if (!string.IsNullOrWhiteSpace(personnelCode)
            && await userManager.Users.AnyAsync(x => x.PersonnelCode == personnelCode, cancellationToken))
            return BadRequest(new { message = "این کد پرسنلی قبلا ثبت شده است" });

        var gender = ParseUserGender(dto.Gender);
        if (dto.Gender is { Length: > 0 } gRaw && gender is null)
            return BadRequest(new { message = "جنسیت معتبر نیست (آقای یا خانم)" });

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
            if (!await db.UserPositions.AnyAsync(x => x.Id == pid && !x.IsDeleted && x.IsActive, cancellationToken))
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
            PersonnelCode = string.IsNullOrWhiteSpace(personnelCode) ? null : personnelCode,
            Gender = gender,
            UserPositionId = positionId,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            PhoneNumberConfirmed = true,
            SignatureDisplayDegree = UserSignatureDisplaySize.NormalizeDegree(dto.SignatureDisplayDegree),
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

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var baseUrl = await frontendUrls.ResolveAdminBaseUrlAsync(cancellationToken);
        var loginUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/login" : $"{baseUrl}/login";
        var welcomeText = await smsPatterns.RenderAsync("user.welcome.create", SmsPatternVars.Dict(
            ("firstName", firstName),
            ("lastName", lastName),
            ("dateStr", dateStr),
            ("timeStr", timeStr),
            ("loginUrl", loginUrl)
        ), cancellationToken);

        await inbox.SendToUserAsync(user.Id, "خوش‌آمدگویی", welcomeText, cancellationToken);

        bool smsSent = false;
        string? smsFailReason = null;

        if (smsSettings.UserCreateSmsEnabled)
        {
            smsSent = await smsSender.SendSmsAsync(new SmsRequest(mobileNumber, welcomeText), cancellationToken);
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

    [HttpPost("import")]
    [Authorize(Policy = "users.import")]
    public async Task<IActionResult> Import([FromBody] ImportUsersDto dto, CancellationToken cancellationToken)
    {
        var groupIds = (dto.DefaultGroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه برای ایمپورت الزامی است" });

        var validGroupIds = await db.UserGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validGroupIds.Count != groupIds.Count)
            return BadRequest(new { message = "یک یا چند گروه انتخاب‌شده معتبر نیستند" });

        var positionsByName = await db.UserPositions
            .Where(x => !x.IsDeleted && x.IsActive)
            .ToDictionaryAsync(x => x.Name.Trim(), x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var invalidRows = new List<object>();
        var candidates = new List<ImportUserCandidate>();

        foreach (var r in dto.Rows)
        {
            var firstName = (r.FirstName ?? "").Trim();
            var lastName = (r.LastName ?? "").Trim();
            var mobile = UserFieldNormalizer.NormalizeMobile(r.MobileNumber);
            var nationalCode = UserFieldNormalizer.NormalizeNationalCode(r.NationalCode);
            var personnelCode = (r.PersonnelCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;

            var gender = ParseUserGender(r.Gender);
            if (r.Gender is { Length: > 0 } && gender is null)
            {
                invalidRows.Add(new { r.RowNumber, reason = "جنسیت نامعتبر (آقای/خانم یا male/female)" });
                continue;
            }
            if (gender is null)
            {
                invalidRows.Add(new { r.RowNumber, reason = "جنسیت الزامی است" });
                continue;
            }

            if (firstName.Length < 2 || lastName.Length < 2)
            {
                invalidRows.Add(new { r.RowNumber, reason = "نام یا نام خانوادگی نامعتبر" });
                continue;
            }
            if (!UserFieldNormalizer.IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, reason = "شماره موبایل نامعتبر" });
                continue;
            }
            if (!UserFieldNormalizer.IsValidNationalCode(nationalCode))
            {
                invalidRows.Add(new { r.RowNumber, reason = "کد ملی نامعتبر است (۱۰ رقم)" });
                continue;
            }

            Guid? positionId = null;
            var positionName = (r.PositionName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(positionName))
            {
                if (!positionsByName.TryGetValue(positionName, out var pid))
                {
                    invalidRows.Add(new { r.RowNumber, reason = $"سمت «{positionName}» در سیستم یافت نشد" });
                    continue;
                }
                positionId = pid;
            }

            candidates.Add(new ImportUserCandidate(
                r.RowNumber, firstName, lastName, mobile, nationalCode, personnelCode, gender.Value, positionId));
        }

        foreach (var dup in candidates.GroupBy(x => x.Mobile).Where(g => g.Count() > 1))
            foreach (var x in dup)
                invalidRows.Add(new { x.RowNumber, reason = "موبایل تکراری در فایل" });

        foreach (var dup in candidates.GroupBy(x => x.NationalCode).Where(g => g.Count() > 1))
            foreach (var x in dup)
                invalidRows.Add(new { x.RowNumber, reason = "کد ملی تکراری در فایل" });

        var uniqueCandidates = candidates
            .Where(c => candidates.Count(x => x.Mobile == c.Mobile) == 1)
            .Where(c => candidates.Count(x => x.NationalCode == c.NationalCode) == 1)
            .ToList();

        if (uniqueCandidates.Count == 0)
        {
            return Ok(new
            {
                message = "ردیف معتبری برای ایمپورت نبود",
                inserted = 0,
                skippedExisting = 0,
                invalidCount = invalidRows.Count,
                invalidRows,
                smsSent = 0,
                smsFailed = 0,
            });
        }

        var mobiles = uniqueCandidates.Select(x => x.Mobile).ToList();
        var nationalCodes = uniqueCandidates.Select(x => x.NationalCode).ToList();
        var personnelCodes = uniqueCandidates
            .Where(x => !string.IsNullOrWhiteSpace(x.PersonnelCode))
            .Select(x => x.PersonnelCode!)
            .Distinct()
            .ToList();

        var existingMobiles = await userManager.Users
            .Where(x => x.PhoneNumber != null && mobiles.Contains(x.PhoneNumber))
            .Select(x => x.PhoneNumber!)
            .ToListAsync(cancellationToken);
        var existingNationalCodes = await userManager.Users
            .Where(x => nationalCodes.Contains(x.NationalCode))
            .Select(x => x.NationalCode)
            .ToListAsync(cancellationToken);
        var existingPersonnelCodes = personnelCodes.Count == 0
            ? new List<string>()
            : await userManager.Users
                .Where(x => x.PersonnelCode != null && personnelCodes.Contains(x.PersonnelCode))
                .Select(x => x.PersonnelCode!)
                .ToListAsync(cancellationToken);

        var existingMobileSet = existingMobiles.ToHashSet(StringComparer.Ordinal);
        var existingNationalSet = existingNationalCodes.ToHashSet(StringComparer.Ordinal);
        var existingPersonnelSet = existingPersonnelCodes.ToHashSet(StringComparer.Ordinal);

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken) ?? new SmsSettings();
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var baseUrl = await frontendUrls.ResolveAdminBaseUrlAsync(cancellationToken);
        var loginUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/login" : $"{baseUrl}/login";

        var inserted = 0;
        var skippedExisting = 0;
        var smsSent = 0;
        var smsFailed = 0;
        var createdUsers = new List<(AppUser User, string WelcomeText)>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var c in uniqueCandidates)
            {
                if (existingMobileSet.Contains(c.Mobile))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "موبایل قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }
                if (existingNationalSet.Contains(c.NationalCode))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "کد ملی قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(c.PersonnelCode) && existingPersonnelSet.Contains(c.PersonnelCode))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "کد پرسنلی قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }

                var generatedPassword = PasswordGenerator.Generate();
                var user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    UserName = c.Mobile,
                    PhoneNumber = c.Mobile,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    NationalCode = c.NationalCode,
                    PersonnelCode = c.PersonnelCode,
                    Gender = c.Gender,
                    UserPositionId = c.PositionId,
                    CreatedByUserId = CurrentUserGuid,
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true,
                    PhoneNumberConfirmed = true,
                    SignatureDisplayDegree = UserSignatureDisplaySize.DefaultDegree,
                };

                var createResult = await userManager.CreateAsync(user, generatedPassword);
                if (!createResult.Succeeded)
                {
                    invalidRows.Add(new
                    {
                        c.RowNumber,
                        reason = string.Join(" | ", createResult.Errors.Select(e => e.Description)),
                    });
                    continue;
                }

                foreach (var gid in validGroupIds)
                    db.UserGroupMembers.Add(new UserGroupMember { UserId = user.Id, GroupId = gid });

                existingMobileSet.Add(c.Mobile);
                existingNationalSet.Add(c.NationalCode);
                if (!string.IsNullOrWhiteSpace(c.PersonnelCode))
                    existingPersonnelSet.Add(c.PersonnelCode);

                var welcomeText = await smsPatterns.RenderAsync("user.welcome.create", SmsPatternVars.Dict(
                    ("firstName", c.FirstName),
                    ("lastName", c.LastName),
                    ("dateStr", dateStr),
                    ("timeStr", timeStr),
                    ("loginUrl", loginUrl)
                ), cancellationToken);
                createdUsers.Add((user, welcomeText));
                inserted++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        foreach (var (user, welcomeText) in createdUsers)
        {
            await inbox.SendToUserAsync(user.Id, "خوش‌آمدگویی", welcomeText, cancellationToken);
            if (!smsSettings.UserCreateSmsEnabled) continue;
            var sent = await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber!, welcomeText), cancellationToken);
            if (sent) smsSent++;
            else smsFailed++;
        }

        return Ok(new
        {
            message = inserted > 0 ? $"{inserted} کاربر ایمپورت شد" : "هیچ کاربر جدیدی ایجاد نشد",
            inserted,
            skippedExisting,
            invalidCount = invalidRows.Count,
            invalidRows,
            smsSent,
            smsFailed,
            smsEnabled = smsSettings.UserCreateSmsEnabled,
        });
    }

    [HttpPost("grouping")]
    [Authorize(Policy = "users.add")]
    public async Task<IActionResult> CreateForGrouping([FromBody] CreateGroupingUserDto dto, CancellationToken cancellationToken)
    {
        if (dto.GroupId == Guid.Empty)
            return BadRequest(new { message = "گروه نامعتبر است" });

        var groupExists = await db.UserGroups.AnyAsync(x => x.Id == dto.GroupId && !x.IsDeleted, cancellationToken);
        if (!groupExists)
            return BadRequest(new { message = "گروه انتخاب‌شده معتبر نیست" });

        var mobileNumber = UserFieldNormalizer.NormalizeMobile(dto.MobileNumber);
        if (!UserFieldNormalizer.IsValidMobile(mobileNumber))
            return BadRequest(new { message = "شماره موبایل معتبر نیست (۰۹ و ۹ رقم)" });

        if (await userManager.Users.AnyAsync(x => x.PhoneNumber == mobileNumber && !x.IsSoftDeleted, cancellationToken))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        var (firstName, lastName, nationalCode, fieldError) = ResolveGroupingUserFields(
            dto.FirstName, dto.LastName, dto.NationalCode);
        if (fieldError is not null)
            return BadRequest(new { message = fieldError });

        if (!string.IsNullOrEmpty(nationalCode)
            && await userManager.Users.AnyAsync(x => x.NationalCode == nationalCode && !x.IsSoftDeleted, cancellationToken))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        var personnelCode = dto.PersonnelCode?.Trim();
        if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;
        if (!string.IsNullOrWhiteSpace(personnelCode)
            && await userManager.Users.AnyAsync(x => x.PersonnelCode == personnelCode && !x.IsSoftDeleted, cancellationToken))
            return BadRequest(new { message = "این کد پرسنلی قبلا ثبت شده است" });

        var generatedPassword = PasswordGenerator.Generate();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = mobileNumber,
            PhoneNumber = mobileNumber,
            FirstName = firstName,
            LastName = lastName,
            NationalCode = nationalCode,
            PersonnelCode = personnelCode,
            Gender = null,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            PhoneNumberConfirmed = true,
            SignatureDisplayDegree = UserSignatureDisplaySize.DefaultDegree,
        };

        var createResult = await userManager.CreateAsync(user, generatedPassword);
        if (!createResult.Succeeded)
            return BadRequest(new
            {
                message = "ایجاد کاربر ناموفق بود",
                errors = createResult.Errors.Select(x => x.Description).ToList()
            });

        db.UserGroupMembers.Add(new UserGroupMember { UserId = user.Id, GroupId = dto.GroupId });
        await db.SaveChangesAsync(cancellationToken);

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken) ?? new SmsSettings();
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var baseUrl = await frontendUrls.ResolveAdminBaseUrlAsync(cancellationToken);
        var loginUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/login" : $"{baseUrl}/login";
        var welcomeText = await smsPatterns.RenderAsync("user.welcome.create", SmsPatternVars.Dict(
            ("firstName", firstName),
            ("lastName", lastName),
            ("dateStr", dateStr),
            ("timeStr", timeStr),
            ("loginUrl", loginUrl)
        ), cancellationToken);

        await inbox.SendToUserAsync(user.Id, "خوش‌آمدگویی", welcomeText, cancellationToken);

        bool smsSent = false;
        if (smsSettings.UserCreateSmsEnabled)
            smsSent = await smsSender.SendSmsAsync(new SmsRequest(mobileNumber, welcomeText), cancellationToken);

        return Ok(new
        {
            id = user.Id,
            message = smsSettings.UserCreateSmsEnabled
                ? smsSent ? "کاربر ساخته شد و پیامک خوش‌آمدگویی ارسال شد" : "کاربر ساخته شد؛ ارسال پیامک ناموفق"
                : "کاربر با موفقیت ساخته شد",
            smsSent,
            smsEnabled = smsSettings.UserCreateSmsEnabled,
        });
    }

    [HttpPost("import-grouping")]
    [Authorize]
    public async Task<IActionResult> ImportGrouping([FromBody] ImportGroupingUsersDto dto, CancellationToken cancellationToken)
    {
        if (!User.HasClaim("permission", "users.import") && !User.HasClaim("permission", "users.add"))
            return Forbid();

        if (dto.GroupId == Guid.Empty)
            return BadRequest(new { message = "گروه نامعتبر است" });

        var groupExists = await db.UserGroups.AnyAsync(x => x.Id == dto.GroupId && !x.IsDeleted, cancellationToken);
        if (!groupExists)
            return BadRequest(new { message = "گروه انتخاب‌شده معتبر نیست" });

        var invalidRows = new List<object>();
        var candidates = new List<GroupingImportCandidate>();

        foreach (var r in dto.Rows)
        {
            var mobile = UserFieldNormalizer.NormalizeMobile(r.MobileNumber);
            if (!UserFieldNormalizer.IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, reason = "شماره موبایل نامعتبر یا خالی" });
                continue;
            }

            var (firstName, lastName, nationalCode, fieldError) = ResolveGroupingUserFields(
                r.FirstName, r.LastName, r.NationalCode);
            if (fieldError is not null)
            {
                invalidRows.Add(new { r.RowNumber, reason = fieldError });
                continue;
            }

            var personnelCode = (r.PersonnelCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;

            candidates.Add(new GroupingImportCandidate(
                r.RowNumber, firstName, lastName, mobile, nationalCode, personnelCode));
        }

        foreach (var dup in candidates.GroupBy(x => x.Mobile).Where(g => g.Count() > 1))
            foreach (var x in dup)
                invalidRows.Add(new { x.RowNumber, reason = "موبایل تکراری در فایل" });

        foreach (var dup in candidates.Where(x => !string.IsNullOrEmpty(x.NationalCode)).GroupBy(x => x.NationalCode).Where(g => g.Count() > 1))
            foreach (var x in dup)
                invalidRows.Add(new { x.RowNumber, reason = "کد ملی تکراری در فایل" });

        var uniqueCandidates = candidates
            .Where(c => candidates.Count(x => x.Mobile == c.Mobile) == 1)
            .Where(c => string.IsNullOrEmpty(c.NationalCode) || candidates.Count(x => x.NationalCode == c.NationalCode) == 1)
            .ToList();

        if (uniqueCandidates.Count == 0)
        {
            return Ok(new
            {
                message = "ردیف معتبری برای ایمپورت نبود",
                inserted = 0,
                skippedExisting = 0,
                invalidCount = invalidRows.Count,
                invalidRows,
                smsSent = 0,
                smsFailed = 0,
            });
        }

        var mobiles = uniqueCandidates.Select(x => x.Mobile).ToList();
        var nationalCodes = uniqueCandidates
            .Where(x => !string.IsNullOrEmpty(x.NationalCode))
            .Select(x => x.NationalCode)
            .ToList();
        var personnelCodes = uniqueCandidates
            .Where(x => !string.IsNullOrWhiteSpace(x.PersonnelCode))
            .Select(x => x.PersonnelCode!)
            .Distinct()
            .ToList();

        var existingMobiles = await userManager.Users
            .Where(x => !x.IsSoftDeleted && x.PhoneNumber != null && mobiles.Contains(x.PhoneNumber))
            .Select(x => x.PhoneNumber!)
            .ToListAsync(cancellationToken);
        var existingNationalCodes = nationalCodes.Count == 0
            ? new List<string>()
            : await userManager.Users
                .Where(x => !x.IsSoftDeleted && nationalCodes.Contains(x.NationalCode))
                .Select(x => x.NationalCode)
                .ToListAsync(cancellationToken);
        var existingPersonnelCodes = personnelCodes.Count == 0
            ? new List<string>()
            : await userManager.Users
                .Where(x => !x.IsSoftDeleted && x.PersonnelCode != null && personnelCodes.Contains(x.PersonnelCode))
                .Select(x => x.PersonnelCode!)
                .ToListAsync(cancellationToken);

        var existingMobileSet = existingMobiles.ToHashSet(StringComparer.Ordinal);
        var existingNationalSet = existingNationalCodes.ToHashSet(StringComparer.Ordinal);
        var existingPersonnelSet = existingPersonnelCodes.ToHashSet(StringComparer.Ordinal);

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken) ?? new SmsSettings();
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var baseUrl = await frontendUrls.ResolveAdminBaseUrlAsync(cancellationToken);
        var loginUrl = string.IsNullOrWhiteSpace(baseUrl) ? "/login" : $"{baseUrl}/login";

        var inserted = 0;
        var skippedExisting = 0;
        var smsSent = 0;
        var smsFailed = 0;
        var createdUsers = new List<(AppUser User, string WelcomeText)>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var c in uniqueCandidates)
            {
                if (existingMobileSet.Contains(c.Mobile))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "موبایل قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }
                if (!string.IsNullOrEmpty(c.NationalCode) && existingNationalSet.Contains(c.NationalCode))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "کد ملی قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(c.PersonnelCode) && existingPersonnelSet.Contains(c.PersonnelCode))
                {
                    invalidRows.Add(new { c.RowNumber, reason = "کد پرسنلی قبلاً ثبت شده" });
                    skippedExisting++;
                    continue;
                }

                var generatedPassword = PasswordGenerator.Generate();
                var user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    UserName = c.Mobile,
                    PhoneNumber = c.Mobile,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    NationalCode = c.NationalCode,
                    PersonnelCode = c.PersonnelCode,
                    Gender = null,
                    CreatedByUserId = CurrentUserGuid,
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true,
                    PhoneNumberConfirmed = true,
                    SignatureDisplayDegree = UserSignatureDisplaySize.DefaultDegree,
                };

                var createResult = await userManager.CreateAsync(user, generatedPassword);
                if (!createResult.Succeeded)
                {
                    invalidRows.Add(new
                    {
                        c.RowNumber,
                        reason = string.Join(" | ", createResult.Errors.Select(e => e.Description)),
                    });
                    continue;
                }

                db.UserGroupMembers.Add(new UserGroupMember { UserId = user.Id, GroupId = dto.GroupId });

                existingMobileSet.Add(c.Mobile);
                if (!string.IsNullOrEmpty(c.NationalCode))
                    existingNationalSet.Add(c.NationalCode);
                if (!string.IsNullOrWhiteSpace(c.PersonnelCode))
                    existingPersonnelSet.Add(c.PersonnelCode);

                var welcomeText = await smsPatterns.RenderAsync("user.welcome.create", SmsPatternVars.Dict(
                    ("firstName", c.FirstName),
                    ("lastName", c.LastName),
                    ("dateStr", dateStr),
                    ("timeStr", timeStr),
                    ("loginUrl", loginUrl)
                ), cancellationToken);
                createdUsers.Add((user, welcomeText));
                inserted++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        foreach (var (user, welcomeText) in createdUsers)
        {
            await inbox.SendToUserAsync(user.Id, "خوش‌آمدگویی", welcomeText, cancellationToken);
            if (!smsSettings.UserCreateSmsEnabled) continue;
            var sent = await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber!, welcomeText), cancellationToken);
            if (sent) smsSent++;
            else smsFailed++;
        }

        return Ok(new
        {
            message = inserted > 0 ? $"{inserted} کاربر ایمپورت شد" : "هیچ کاربر جدیدی ایجاد نشد",
            inserted,
            skippedExisting,
            invalidCount = invalidRows.Count,
            invalidRows,
            smsSent,
            smsFailed,
            smsEnabled = smsSettings.UserCreateSmsEnabled,
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
        [FromQuery] bool lite = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var query = db.Users.AsNoTracking().Where(x => !x.IsSoftDeleted);
        if (!CanReadAllUsers)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x =>
                (x.FirstName + " " + x.LastName).Contains(q) ||
                (x.PhoneNumber != null && x.PhoneNumber.Contains(q)) ||
                x.NationalCode.Contains(q));
        }

        if (status == "active") query = query.Where(x => x.IsActive);
        if (status == "inactive") query = query.Where(x => !x.IsActive);

        if (groupId is Guid gid && gid != Guid.Empty)
            query = query.Where(u => u.GroupMembers.Any(m => m.GroupId == gid));

        query = sortBy switch
        {
            "created_asc" => query.OrderBy(x => x.CreatedAtUtc),
            "name_asc" => query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName),
            "name_desc" => query.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };

        // DbContext is not thread-safe — count and page queries must run sequentially.
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
                x.PersonnelCode,
                x.Gender,
                x.CreatedAtUtc,
                x.IsActive,
                x.UserPositionId,
                x.SignatureImagePath,
                x.SignatureDisplayDegree,
            })
            .ToListAsync(cancellationToken);

        var positionIds = usersRaw
            .Where(x => x.UserPositionId.HasValue)
            .Select(x => x.UserPositionId!.Value)
            .Distinct()
            .ToList();
        var positionsById = positionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.UserPositions.AsNoTracking()
                .Where(x => positionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        if (lite)
        {
            var liteItems = usersRaw.Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.MobileNumber,
                u.NationalCode,
                PersonnelCode = u.PersonnelCode,
                Gender = u.Gender == UserGender.Male ? "male" : u.Gender == UserGender.Female ? "female" : null,
                u.CreatedAtUtc,
                u.IsActive,
                u.UserPositionId,
                UserPositionName = u.UserPositionId.HasValue && positionsById.TryGetValue(u.UserPositionId.Value, out var pn) ? pn : null,
                HasSignature = u.SignatureImagePath != null && u.SignatureImagePath != "",
                u.SignatureDisplayDegree,
            }).ToList();

            return Ok(new
            {
                items = liteItems,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
            });
        }

        var userIds = usersRaw.Select(x => x.Id).ToList();
        var userRoles = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Join(db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var rolesByUser = userRoles.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var userGroups = await db.UserGroupMembers.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Join(db.UserGroups.AsNoTracking(), ug => ug.GroupId, g => g.Id, (ug, g) => new UserGroupOptionDto(ug.UserId, g.Id, g.Name))
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
                SignatureDisplayDegree = u.SignatureDisplayDegree,
                SignatureWidthPx = UserSignatureDisplaySize.WidthPxFromDegree(u.SignatureDisplayDegree),
                RoleName = roles.FirstOrDefault() ?? "",
                RoleNames = roles,
                Groups = groupNames,
            };
        }).ToList();

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
        });
    }

    [HttpGet("{id:guid}/group-ids")]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> GetUserGroupIds(Guid id, CancellationToken cancellationToken)
    {
        var exists = await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && !x.IsSoftDeleted, cancellationToken);
        if (!exists) return NotFound();

        var groupIds = await db.UserGroupMembers.AsNoTracking()
            .Where(x => x.UserId == id)
            .Select(x => x.GroupId)
            .ToListAsync(cancellationToken);

        return Ok(groupIds);
    }

    [HttpGet("{id:guid}/role-names")]
    [Authorize(Policy = "users.access.read")]
    public async Task<IActionResult> GetUserRoleNames(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsSoftDeleted) return NotFound();
        var roles = await userManager.GetRolesAsync(user);
        return Ok(roles.ToList());
    }

    [HttpGet("roles-options")]
    [Authorize(Policy = "users.access.read")]
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
                PositionName = x.UserPosition != null ? x.UserPosition.Name : null,
                HasSignature = x.SignatureImagePath != null && x.SignatureImagePath != "",
            })
            .ToListAsync(cancellationToken);
        return Ok(users.Select(u => new
        {
            u.Id,
            u.FirstName,
            u.LastName,
            u.Name,
            u.Email,
            AvatarUrl = ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, u.Id, u.AvatarUrl),
            u.PositionName,
            u.HasSignature,
        }));
    }

    [HttpGet("{id:guid}/edit-profile")]
    [Authorize(Policy = "users.read")]
    public async Task<IActionResult> GetEditProfile(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(x => x.Id == id && !x.IsSoftDeleted)
            .Select(x => new
            {
                x.FirstName,
                x.LastName,
                MobileNumber = x.PhoneNumber ?? "",
                x.NationalCode,
                x.PersonnelCode,
                x.Gender,
                x.UserPositionId,
                x.SignatureDisplayDegree,
                HasSignature = x.SignatureImagePath != null && x.SignatureImagePath != "",
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null) return NotFound();

        var groupIds = await db.UserGroupMembers.AsNoTracking()
            .Where(x => x.UserId == id)
            .Select(x => x.GroupId)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            user.FirstName,
            user.LastName,
            user.MobileNumber,
            user.NationalCode,
            PersonnelCode = user.PersonnelCode ?? "",
            Gender = user.Gender == UserGender.Male ? "male" : user.Gender == UserGender.Female ? "female" : "",
            user.UserPositionId,
            user.SignatureDisplayDegree,
            user.HasSignature,
            GroupIds = groupIds,
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "users.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var firstName = dto.FirstName.Trim();
        var lastName = dto.LastName.Trim();
        var mobileNumber = UserFieldNormalizer.NormalizeMobile(dto.MobileNumber);
        var nationalCode = UserFieldNormalizer.NormalizeNationalCode(dto.NationalCode);

        if (firstName.Length < 2 || lastName.Length < 2)
            return BadRequest(new { message = "نام و نام خانوادگی باید حداقل 2 کاراکتر باشند" });
        if (!UserFieldNormalizer.IsValidMobile(mobileNumber))
            return BadRequest(new { message = "شماره موبایل معتبر نیست (۰۹ و ۹ رقم)" });
        if (!UserFieldNormalizer.IsValidNationalCode(nationalCode))
            return BadRequest(new { message = "کد ملی باید ۱۰ رقم باشد" });

        if (await userManager.Users.AnyAsync(x => x.PhoneNumber == mobileNumber && x.Id != id))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });
        if (await userManager.Users.AnyAsync(x => x.NationalCode == nationalCode && x.Id != id))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = mobileNumber;
        user.UserName = mobileNumber;
        user.NationalCode = nationalCode;

        var personnelCode = dto.PersonnelCode?.Trim();
        if (!string.IsNullOrWhiteSpace(personnelCode)
            && await userManager.Users.AnyAsync(x => x.PersonnelCode == personnelCode && x.Id != id))
            return BadRequest(new { message = "این کد پرسنلی قبلا ثبت شده است" });
        user.PersonnelCode = string.IsNullOrWhiteSpace(personnelCode) ? null : personnelCode;

        if (dto.Gender is { Length: > 0 } gRaw)
        {
            var parsed = ParseUserGender(gRaw);
            if (parsed is null)
                return BadRequest(new { message = "جنسیت معتبر نیست (آقای یا خانم)" });
            user.Gender = parsed;
        }

        if (dto.UserPositionId is Guid pid && pid != Guid.Empty)
        {
            if (!await db.UserPositions.AnyAsync(x => x.Id == pid && !x.IsDeleted && x.IsActive))
                return BadRequest(new { message = "سمت انتخاب‌شده معتبر نیست" });
            user.UserPositionId = pid;
        }
        else
            user.UserPositionId = null;

        if (dto.SignatureDisplayDegree.HasValue)
            user.SignatureDisplayDegree = UserSignatureDisplaySize.NormalizeDegree(dto.SignatureDisplayDegree);

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "بروزرسانی کاربر ناموفق بود",
                errors = result.Errors.Select(x => x.Description).ToList(),
            });
        }

        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var validGroupIds = await db.UserGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();
        if (validGroupIds.Count != groupIds.Count)
            return BadRequest(new { message = "یک یا چند گروه انتخاب‌شده معتبر نیستند" });

        var oldMembers = await db.UserGroupMembers.Where(x => x.UserId == id).ToListAsync();
        db.UserGroupMembers.RemoveRange(oldMembers);
        foreach (var gid in validGroupIds)
            db.UserGroupMembers.Add(new UserGroupMember { UserId = id, GroupId = gid });
        await db.SaveChangesAsync();
        return Ok(new { message = "مشخصات کاربر بروزرسانی شد" });
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Policy = "users.access.update")]
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
    [Authorize(Policy = "users.access.update")]
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

        if (form.SignatureDisplayDegree.HasValue)
            user.SignatureDisplayDegree = UserSignatureDisplaySize.NormalizeDegree(form.SignatureDisplayDegree);

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return BadRequest(update.Errors);
        return Ok(new
        {
            message = "امضای دیجیتال ذخیره شد",
            signaturePath = user.SignatureImagePath,
            signatureDisplayDegree = user.SignatureDisplayDegree,
            signatureWidthPx = UserSignatureDisplaySize.WidthPxFromDegree(user.SignatureDisplayDegree),
        });
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
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserGuid == id)
            return BadRequest(new { message = "امکان حذف حساب کاربری خودتان وجود ندارد" });

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new { message = "کاربر یافت نشد" });
        if (user.IsSoftDeleted)
            return BadRequest(new { message = "این کاربر قبلاً حذف شده است" });

        var now = DateTime.UtcNow;
        var activeTokens = await db.RefreshTokens
            .Where(x => x.UserId == id && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens)
            token.RevokedAtUtc = now;

        user.IsSoftDeleted = true;
        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = "حذف کاربر ناموفق بود", errors = update.Errors.Select(e => e.Description) });

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "کاربر حذف شد؛ ورود و تمدید نشست مجاز نیست" });
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

    private static UserGender? ParseUserGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "male" or "m" or "1" or "آقا" or "آقای" or "mr" => UserGender.Male,
            "female" or "f" or "2" or "خانم" or "ms" => UserGender.Female,
            _ => null,
        };
    }

    private static (string FirstName, string LastName, string NationalCode, string? Error) ResolveGroupingUserFields(
        string? firstNameRaw,
        string? lastNameRaw,
        string? nationalCodeRaw)
    {
        var firstName = (firstNameRaw ?? "").Trim();
        var lastName = (lastNameRaw ?? "").Trim();
        var nationalCode = UserFieldNormalizer.NormalizeNationalCode(nationalCodeRaw);

        if (firstName.Length < 2) firstName = "کاربر";
        if (lastName.Length < 2) lastName = "نامشخص";

        if (!string.IsNullOrWhiteSpace(nationalCodeRaw) && !UserFieldNormalizer.IsValidNationalCode(nationalCode))
            return ("", "", "", "کد ملی نامعتبر است (۱۰ رقم)");

        return (firstName, lastName, nationalCode, null);
    }

    private sealed record ImportUserCandidate(
        int RowNumber,
        string FirstName,
        string LastName,
        string Mobile,
        string NationalCode,
        string? PersonnelCode,
        UserGender Gender,
        Guid? PositionId);

    private sealed record GroupingImportCandidate(
        int RowNumber,
        string FirstName,
        string LastName,
        string Mobile,
        string NationalCode,
        string? PersonnelCode);
}

public record UserGroupOptionDto(Guid UserId, Guid Id, string Name);

public sealed class UploadUserSignatureForm
{
    public IFormFile? Signature { get; set; }
    public IFormFile? File { get; set; }
    public int? SignatureDisplayDegree { get; set; }
}

