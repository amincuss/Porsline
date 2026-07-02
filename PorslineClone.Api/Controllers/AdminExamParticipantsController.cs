using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/exam-participants")]
[Authorize]
public class AdminExamParticipantsController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserGuid =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    private bool CanReadAll => User.HasClaim("permission", "exams.read.all");

    [HttpGet]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] string? search = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] string? sortBy = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var query = db.ExamParticipants.AsNoTracking().AsQueryable();
        if (!CanReadAll)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }

        if (groupId is Guid gid && gid != Guid.Empty)
            query = query.Where(p => p.GroupMembers.Any(m => m.GroupId == gid));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x =>
                (x.FirstName + " " + x.LastName).Contains(q) ||
                x.MobileNumber.Contains(q) ||
                x.NationalCode.Contains(q) ||
                (x.PersonnelCode != null && x.PersonnelCode.Contains(q)));
        }

        query = sortBy switch
        {
            "name_asc" => query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName),
            "name_desc" => query.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName),
            "created_asc" => query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                MobileNumber = x.MobileNumber,
                x.NationalCode,
                x.PersonnelCode,
                x.IsActive,
                x.CreatedAtUtc,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await db.ExamParticipants.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                MobileNumber = x.MobileNumber,
                x.NationalCode,
                PersonnelCode = x.PersonnelCode ?? "",
                x.IsActive,
                GroupIds = x.GroupMembers.Select(m => m.GroupId).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = "exams.add")]
    public async Task<IActionResult> Create([FromBody] CreateExamParticipantDto dto, CancellationToken ct)
    {
        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0 && dto.GroupId is Guid gid && gid != Guid.Empty)
            groupIds.Add(gid);
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var validGroupIds = await db.ExamParticipantGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (validGroupIds.Count != groupIds.Count)
            return BadRequest(new { message = "یک یا چند گروه انتخاب‌شده معتبر نیستند" });

        var mobile = UserFieldNormalizer.NormalizeMobile(dto.MobileNumber);
        if (!UserFieldNormalizer.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل معتبر نیست (۰۹ و ۹ رقم)" });

        if (await db.ExamParticipants.AnyAsync(x => x.MobileNumber == mobile, ct))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        var (firstName, lastName, nationalCode, fieldError) = ResolveFields(
            dto.FirstName, dto.LastName, dto.NationalCode);
        if (fieldError is not null)
            return BadRequest(new { message = fieldError });

        var personnelCode = dto.PersonnelCode?.Trim();
        if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;

        if (!string.IsNullOrEmpty(nationalCode)
            && await db.ExamParticipants.AnyAsync(x => x.NationalCode == nationalCode, ct))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        if (!string.IsNullOrWhiteSpace(personnelCode)
            && await db.ExamParticipants.AnyAsync(x => x.PersonnelCode == personnelCode, ct))
            return BadRequest(new { message = "این کد پرسنلی قبلا ثبت شده است" });

        var entity = new ExamParticipant
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            MobileNumber = mobile,
            NationalCode = nationalCode,
            PersonnelCode = personnelCode,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = dto.IsActive ?? true,
        };
        foreach (var g in validGroupIds)
            entity.GroupMembers.Add(new ExamParticipantGroupMember { ParticipantId = entity.Id, GroupId = g });

        db.ExamParticipants.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new { id = entity.Id, message = "آزمون‌دهنده ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExamParticipantDto dto, CancellationToken ct)
    {
        var entity = await db.ExamParticipants
            .Include(x => x.GroupMembers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();

        var mobile = UserFieldNormalizer.NormalizeMobile(dto.MobileNumber);
        if (!UserFieldNormalizer.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل معتبر نیست" });

        if (await db.ExamParticipants.AnyAsync(x => x.MobileNumber == mobile && x.Id != id, ct))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        var (firstName, lastName, nationalCode, fieldError) = ResolveFields(
            dto.FirstName, dto.LastName, dto.NationalCode);
        if (fieldError is not null)
            return BadRequest(new { message = fieldError });

        var personnelCode = dto.PersonnelCode?.Trim();
        if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;

        if (!string.IsNullOrEmpty(nationalCode)
            && await db.ExamParticipants.AnyAsync(x => x.NationalCode == nationalCode && x.Id != id, ct))
            return BadRequest(new { message = "این کد ملی قبلا ثبت شده است" });

        if (!string.IsNullOrWhiteSpace(personnelCode)
            && await db.ExamParticipants.AnyAsync(x => x.PersonnelCode == personnelCode && x.Id != id, ct))
            return BadRequest(new { message = "این کد پرسنلی قبلا ثبت شده است" });

        entity.FirstName = firstName;
        entity.LastName = lastName;
        entity.MobileNumber = mobile;
        entity.NationalCode = nationalCode;
        entity.PersonnelCode = personnelCode;
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var validGroupIds = await db.ExamParticipantGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (validGroupIds.Count != groupIds.Count)
            return BadRequest(new { message = "یک یا چند گروه انتخاب‌شده معتبر نیستند" });

        db.ExamParticipantGroupMembers.RemoveRange(entity.GroupMembers);
        foreach (var gid in validGroupIds)
            entity.GroupMembers.Add(new ExamParticipantGroupMember { ParticipantId = id, GroupId = gid });

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "آزمون‌دهنده بروزرسانی شد" });
    }

    [HttpPost("import")]
    [Authorize(Policy = "exams.add")]
    public async Task<IActionResult> Import([FromBody] ImportExamParticipantsDto dto, CancellationToken ct)
    {
        if (dto.GroupId == Guid.Empty)
            return BadRequest(new { message = "گروه نامعتبر است" });

        if (!await db.ExamParticipantGroups.AnyAsync(x => x.Id == dto.GroupId, ct))
            return BadRequest(new { message = "گروه انتخاب‌شده معتبر نیست" });

        var invalidRows = new List<object>();
        var candidates = new List<ImportCandidate>();

        foreach (var r in dto.Rows)
        {
            var mobile = UserFieldNormalizer.NormalizeMobile(r.MobileNumber);
            if (!UserFieldNormalizer.IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, reason = "شماره موبایل نامعتبر یا خالی" });
                continue;
            }

            var (firstName, lastName, nationalCode, fieldError) = ResolveFields(
                r.FirstName, r.LastName, r.NationalCode);
            if (fieldError is not null)
            {
                invalidRows.Add(new { r.RowNumber, reason = fieldError });
                continue;
            }

            var personnelCode = (r.PersonnelCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(personnelCode)) personnelCode = null;

            candidates.Add(new ImportCandidate(
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

        var existingMobiles = await db.ExamParticipants
            .Where(x => mobiles.Contains(x.MobileNumber))
            .Select(x => x.MobileNumber)
            .ToListAsync(ct);
        var existingNationalCodes = nationalCodes.Count == 0
            ? new List<string>()
            : await db.ExamParticipants
                .Where(x => nationalCodes.Contains(x.NationalCode))
                .Select(x => x.NationalCode)
                .ToListAsync(ct);
        var existingPersonnelCodes = personnelCodes.Count == 0
            ? new List<string>()
            : await db.ExamParticipants
                .Where(x => x.PersonnelCode != null && personnelCodes.Contains(x.PersonnelCode))
                .Select(x => x.PersonnelCode!)
                .ToListAsync(ct);

        var existingMobileSet = existingMobiles.ToHashSet(StringComparer.Ordinal);
        var existingNationalSet = existingNationalCodes.ToHashSet(StringComparer.Ordinal);
        var existingPersonnelSet = existingPersonnelCodes.ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        var skippedExisting = 0;

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

            var entity = new ExamParticipant
            {
                Id = Guid.NewGuid(),
                FirstName = c.FirstName,
                LastName = c.LastName,
                MobileNumber = c.Mobile,
                NationalCode = c.NationalCode,
                PersonnelCode = c.PersonnelCode,
                CreatedByUserId = CurrentUserGuid,
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true,
            };
            entity.GroupMembers.Add(new ExamParticipantGroupMember
            {
                ParticipantId = entity.Id,
                GroupId = dto.GroupId,
            });
            db.ExamParticipants.Add(entity);

            existingMobileSet.Add(c.Mobile);
            if (!string.IsNullOrEmpty(c.NationalCode))
                existingNationalSet.Add(c.NationalCode);
            if (!string.IsNullOrWhiteSpace(c.PersonnelCode))
                existingPersonnelSet.Add(c.PersonnelCode);
            inserted++;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = inserted > 0 ? $"{inserted} آزمون‌دهنده ایمپورت شد" : "هیچ ردیف جدیدی ایجاد نشد",
            inserted,
            skippedExisting,
            invalidCount = invalidRows.Count,
            invalidRows,
        });
    }

    private static (string FirstName, string LastName, string NationalCode, string? Error) ResolveFields(
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

    private sealed record ImportCandidate(
        int RowNumber,
        string FirstName,
        string LastName,
        string Mobile,
        string NationalCode,
        string? PersonnelCode);
}

public record CreateExamParticipantDto(
    string MobileNumber,
    string? FirstName = null,
    string? LastName = null,
    string? NationalCode = null,
    string? PersonnelCode = null,
    Guid? GroupId = null,
    List<Guid>? GroupIds = null,
    bool? IsActive = null);

public record UpdateExamParticipantDto(
    string MobileNumber,
    string? FirstName = null,
    string? LastName = null,
    string? NationalCode = null,
    string? PersonnelCode = null,
    List<Guid>? GroupIds = null,
    bool? IsActive = null);

public record ImportExamParticipantsDto(Guid GroupId, List<ImportExamParticipantRowDto> Rows);
public record ImportExamParticipantRowDto(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? NationalCode,
    string? PersonnelCode = null);
