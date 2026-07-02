using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responders")]
[Authorize]
public class AdminRespondersController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserGuid
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : null;
        }
    }
    private bool CanReadAllResponders => User.HasClaim("permission", "responders.read.all");

    private static IQueryable<Responder> ActiveOnly(IQueryable<Responder> query) =>
        query.Where(x => !x.IsDeleted);

    [HttpGet("lookup")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Lookup(
        [FromQuery] string? nationalCode,
        [FromQuery] string? prefix,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(nationalCode))
        {
            var code = nationalCode.Trim();
            if (!ResponderLookupHelper.IsValidNationalCode(code))
                return BadRequest(new { message = "کد ملی الزامی است" });

            var q = ActiveOnly(db.Responders.AsQueryable());
            if (!CanReadAllResponders)
            {
                var creatorId = CurrentUserGuid;
                if (!creatorId.HasValue) return Ok(new { found = false, item = (object?)null });
                q = q.Where(x => x.CreatedByUserId == creatorId.Value);
            }

            var item = await q.FirstOrDefaultAsync(x => x.NationalCode == code, ct);
            return Ok(new
            {
                found = item is not null,
                item = item is null ? null : MapLookupItem(item),
            });
        }

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            var p = prefix.Trim();
            if (p.Length < 3)
                return Ok(Array.Empty<object>());

            var q = ActiveOnly(db.Responders.AsQueryable());
            if (!CanReadAllResponders)
            {
                var creatorId = CurrentUserGuid;
                if (!creatorId.HasValue) return Ok(Array.Empty<object>());
                q = q.Where(x => x.CreatedByUserId == creatorId.Value);
            }

            var items = await q
                .Where(x => x.NationalCode.StartsWith(p))
                .OrderBy(x => x.NationalCode)
                .Take(10)
                .Select(x => new
                {
                    x.Id,
                    x.NationalCode,
                    x.FullName,
                    x.MobileNumber,
                    x.Gender,
                })
                .ToListAsync(ct);
            return Ok(items);
        }

        return BadRequest(new { message = "nationalCode یا prefix الزامی است" });
    }

    private static object MapLookupItem(Responder x) => new
    {
        x.Id,
        x.NationalCode,
        x.FullName,
        x.MobileNumber,
        x.Gender,
    };

    [HttpGet("export")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var query = ActiveOnly(db.Responders.AsQueryable());
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue) return Ok(new List<object>());
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }

        var items = await query
            .OrderBy(x => x.FullName)
            .Select(x => new
            {
                x.FullName,
                x.MobileNumber,
                x.NationalCode,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("options")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Options([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var q = ActiveOnly(db.Responders.AsQueryable());
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue) return Ok(new List<object>());
            q = q.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.FullName.Contains(s) || x.MobileNumber.Contains(s) || x.NationalCode.Contains(s));
        }
        var items = await q
            .OrderBy(x => x.FullName)
            .Take(50)
            .Select(x => new { x.Id, x.FullName, x.MobileNumber, x.NationalCode, x.Gender })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] Guid? groupId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = ActiveOnly(db.Responders.AsQueryable());
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        if (groupId is Guid gid && gid != Guid.Empty)
            query = query.Where(x => x.GroupMembers.Any(m => m.GroupId == gid));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.FullName.Contains(q) || x.MobileNumber.Contains(q) || x.NationalCode.Contains(q));
        }

        query = sortBy switch
        {
            "name_asc" => query.OrderBy(x => x.FullName),
            "name_desc" => query.OrderByDescending(x => x.FullName),
            "mobile_asc" => query.OrderBy(x => x.MobileNumber),
            "mobile_desc" => query.OrderByDescending(x => x.MobileNumber),
            "created_asc" => query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ResponderItemDto(
                x.Id,
                x.FullName,
                x.MobileNumber,
                x.NationalCode,
                x.Gender,
                x.CreatedAtUtc,
                x.GroupMembers.Select(gm => new ResponderGroupOptionDto(gm.GroupId, gm.Group.Name)).ToList()
            ))
            .ToListAsync(ct);

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpPost]
    [Authorize(Policy = "responders.add")]
    public async Task<IActionResult> Create([FromBody] CreateResponderDto dto, CancellationToken ct)
    {
        var fullName = ResolveFullName(dto.FullName, dto.FirstName, dto.LastName);
        var mobile = dto.MobileNumber.Trim();
        var nationalCode = dto.NationalCode.Trim();

        if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
        var (firstName, lastName, _) = ResponderNameHelper.SplitFullName(fullName);
        if (firstName.Length < 2) return BadRequest(new { message = "نام نامعتبر است" });
        if (lastName.Length < 2) return BadRequest(new { message = "نام خانوادگی نامعتبر است" });
        if (!ResponderLookupHelper.IsValidNationalCode(nationalCode))
            return BadRequest(new { message = "کد ملی الزامی است" });
        if (!ResponderLookupHelper.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل معتبر نیست" });
        try
        {
            await ResponderLookupHelper.EnsureNationalCodeUniqueAsync(db, null, nationalCode, ct);
            await ResponderLookupHelper.EnsureMobileUniqueAsync(db, null, mobile, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var gender = ResponderHonorific.ParseGender(dto.Gender);
        if (dto.Gender is { Length: > 0 } && gender is null)
            return BadRequest(new { message = "جنسیت معتبر نیست (آقای یا خانم)" });
        if (gender is null)
            return BadRequest(new { message = "جنسیت (آقای/خانم) الزامی است" });

        var validGroups = await ResolveExistingGroupIdsAsync(groupIds, ct);
        if (validGroups.Count == 0)
            return BadRequest(new { message = "گروه انتخاب‌شده معتبر نیست" });

        var entity = new Responder
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            MobileNumber = mobile,
            NationalCode = ResponderLookupHelper.NormalizeNationalCode(nationalCode),
            Gender = gender,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow
        };
        foreach (var gid in validGroups)
            entity.GroupMembers.Add(new ResponderGroupMember { ResponderId = entity.Id, GroupId = gid });
        db.Responders.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخگو با موفقیت ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateResponderDto dto, CancellationToken ct)
    {
        var item = await db.Responders
            .Include(x => x.GroupMembers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "پاسخگو یافت نشد" });

        var fullName = ResolveFullName(dto.FullName, dto.FirstName, dto.LastName);
        var mobile = dto.MobileNumber.Trim();
        var nationalCode = dto.NationalCode.Trim();
        if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
        var (firstName, lastName, _) = ResponderNameHelper.SplitFullName(fullName);
        if (firstName.Length < 2) return BadRequest(new { message = "نام نامعتبر است" });
        if (lastName.Length < 2) return BadRequest(new { message = "نام خانوادگی نامعتبر است" });
        if (!ResponderLookupHelper.IsValidNationalCode(nationalCode))
            return BadRequest(new { message = "کد ملی الزامی است" });
        if (!ResponderLookupHelper.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل معتبر نیست" });
        try
        {
            await ResponderLookupHelper.EnsureNationalCodeUniqueAsync(db, id, nationalCode, ct);
            await ResponderLookupHelper.EnsureMobileUniqueAsync(db, id, mobile, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var gender = ResponderHonorific.ParseGender(dto.Gender);
        if (dto.Gender is { Length: > 0 } gRaw && gender is null)
            return BadRequest(new { message = "جنسیت معتبر نیست (آقای یا خانم)" });

        item.FullName = fullName;
        item.MobileNumber = mobile;
        item.NationalCode = ResponderLookupHelper.NormalizeNationalCode(nationalCode);
        if (gender is not null)
            item.Gender = gender;
        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        item.GroupMembers.Clear();
        if (groupIds.Count > 0)
        {
            var validGroups = await ResolveExistingGroupIdsAsync(groupIds, ct);
            foreach (var gid in validGroups)
                item.GroupMembers.Add(new ResponderGroupMember { ResponderId = item.Id, GroupId = gid });
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخگو بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "responders.delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var item = await db.Responders
            .Include(x => x.GroupMembers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "پاسخگو یافت نشد" });
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue || item.CreatedByUserId != creatorId.Value)
                return NotFound(new { message = "پاسخگو یافت نشد" });
        }

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        item.GroupMembers.Clear();
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخگو حذف شد" });
    }

    [HttpPost("import")]
    [Authorize(Policy = "responders.add")]
    public async Task<IActionResult> Import([FromBody] ImportRespondersDto dto, CancellationToken ct)
    {
        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return BadRequest(new { message = "انتخاب حداقل یک گروه الزامی است" });

        var validGroups = await ResolveExistingGroupIdsAsync(groupIds, ct);
        if (validGroups.Count == 0)
            return BadRequest(new { message = "گروه انتخاب‌شده معتبر نیست" });

        var invalidRows = new List<object>();
        var valid = new List<(string Name, string Mobile, string NationalCode, UserGender? Gender, int Row)>();

        foreach (var r in dto.Rows)
        {
            var name = ResolveImportFullName(r);
            var mobile = (r.MobileNumber ?? "").Trim();
            var nationalCode = (r.NationalCode ?? "").Trim();

            if (name.Length == 0 && mobile.Length == 0 && nationalCode.Length == 0
                && string.IsNullOrWhiteSpace(r.Gender))
                continue;

            if (name.Length > 0 && name.Length < 2)
            {
                invalidRows.Add(new { r.RowNumber, reason = "نام نامعتبر" });
                continue;
            }
            if (mobile.Length > 0 && !ResponderLookupHelper.IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, reason = "شماره موبایل نامعتبر" });
                continue;
            }
            if (nationalCode.Length > 0 && !ResponderLookupHelper.IsValidNationalCode(nationalCode))
            {
                invalidRows.Add(new { r.RowNumber, reason = "کد ملی نامعتبر" });
                continue;
            }
            if (nationalCode.Length == 0 && !ResponderLookupHelper.IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, reason = "حداقل کد ملی یا موبایل معتبر لازم است" });
                continue;
            }

            UserGender? gender = null;
            if (r.Gender is { Length: > 0 } gRaw)
            {
                gender = ResponderHonorific.ParseGender(gRaw);
                if (gender is null)
                {
                    invalidRows.Add(new { r.RowNumber, reason = "جنسیت نامعتبر" });
                    continue;
                }
            }

            valid.Add((
                name,
                mobile,
                nationalCode.Length > 0 ? ResponderLookupHelper.NormalizeNationalCode(nationalCode) : "",
                gender,
                r.RowNumber));
        }

        var duplicateRowNumbers = new HashSet<int>();
        foreach (var dup in valid.Where(x => !string.IsNullOrEmpty(x.NationalCode)).GroupBy(x => x.NationalCode).Where(g => g.Count() > 1))
        {
            foreach (var x in dup)
            {
                duplicateRowNumbers.Add(x.Row);
                invalidRows.Add(new { x.Row, reason = "کد ملی تکراری در فایل" });
            }
        }
        foreach (var dup in valid.Where(x => ResponderLookupHelper.IsValidMobile(x.Mobile)).GroupBy(x => x.Mobile).Where(g => g.Count() > 1))
        {
            foreach (var x in dup)
            {
                duplicateRowNumbers.Add(x.Row);
                invalidRows.Add(new { x.Row, reason = "موبایل تکراری در فایل" });
            }
        }

        var uniqueValid = valid.Where(v => !duplicateRowNumbers.Contains(v.Row)).ToList();

        var inserted = 0;
        var updated = 0;
        foreach (var v in uniqueValid)
        {
            var existing = await FindImportResponderAsync(v.NationalCode, v.Mobile, ct);

            if (existing is not null)
            {
                await ApplyImportRowAsync(existing, v, validGroups, ct);
                updated++;
            }
            else if (!ResponderLookupHelper.IsValidMobile(v.Mobile))
            {
                invalidRows.Add(new { v.Row, reason = "برای ثبت جدید موبایل معتبر لازم است" });
                continue;
            }
            else
            {
                var entity = new Responder
                {
                    Id = Guid.NewGuid(),
                    FullName = v.Name.Length >= 2 ? v.Name : v.Mobile,
                    MobileNumber = v.Mobile,
                    NationalCode = v.NationalCode,
                    Gender = v.Gender,
                    CreatedByUserId = CurrentUserGuid,
                    CreatedAtUtc = DateTime.UtcNow
                };
                foreach (var gid in validGroups)
                    entity.GroupMembers.Add(new ResponderGroupMember { ResponderId = entity.Id, GroupId = gid });
                db.Responders.Add(entity);
                inserted++;
            }

            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            message = "ایمپورت انجام شد",
            inserted,
            updated,
            invalidCount = invalidRows.Count,
            invalidRows
        });
    }

    private async Task<Responder?> FindImportResponderAsync(string nationalCode, string mobile, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(nationalCode))
        {
            var byCode = await ActiveOnly(db.Responders)
                .Include(x => x.GroupMembers)
                .FirstOrDefaultAsync(x => x.NationalCode == nationalCode, ct);
            if (byCode is not null)
                return byCode;
        }

        if (ResponderLookupHelper.IsValidMobile(mobile))
        {
            return await ActiveOnly(db.Responders)
                .Include(x => x.GroupMembers)
                .FirstOrDefaultAsync(x => x.MobileNumber == mobile, ct);
        }

        return null;
    }

    private async Task ApplyImportRowAsync(
        Responder existing,
        (string Name, string Mobile, string NationalCode, UserGender? Gender, int Row) v,
        List<Guid> validGroups,
        CancellationToken ct)
    {
        if (v.Name.Length >= 2)
            existing.FullName = v.Name;

        if (ResponderLookupHelper.IsValidMobile(v.Mobile)
            && v.Mobile != existing.MobileNumber
            && !await ActiveOnly(db.Responders).AnyAsync(x => x.MobileNumber == v.Mobile && x.Id != existing.Id, ct))
            existing.MobileNumber = v.Mobile;

        if (!string.IsNullOrEmpty(v.NationalCode)
            && v.NationalCode != existing.NationalCode
            && !await ActiveOnly(db.Responders).AnyAsync(x => x.NationalCode == v.NationalCode && x.Id != existing.Id, ct))
            existing.NationalCode = v.NationalCode;

        if (v.Gender is not null)
            existing.Gender = v.Gender;

        foreach (var gid in validGroups)
        {
            if (existing.GroupMembers.All(m => m.GroupId != gid))
                existing.GroupMembers.Add(new ResponderGroupMember { ResponderId = existing.Id, GroupId = gid });
        }
    }

    private async Task<List<Guid>> ResolveExistingGroupIdsAsync(IReadOnlyList<Guid> groupIds, CancellationToken ct)
    {
        var distinct = groupIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) return [];

        var found = new List<Guid>(distinct.Count);
        foreach (var id in distinct)
        {
            if (await db.ResponderGroups.AsNoTracking().AnyAsync(x => x.Id == id, ct))
                found.Add(id);
        }

        return found;
    }

    private static string ResolveImportFullName(ImportResponderRowDto row) =>
        ResolveFullName(row.FullName, row.FirstName, row.LastName);

    private static string ResolveFullName(string? fullName, string? firstName, string? lastName)
    {
        var explicitFull = (fullName ?? "").Trim();
        if (explicitFull.Length >= 2)
            return explicitFull;

        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        return $"{first} {last}".Trim();
    }

}

public record ResponderGroupOptionDto(Guid Id, string Name);
public record ResponderItemDto(Guid Id, string FullName, string MobileNumber, string NationalCode, UserGender? Gender, DateTime CreatedAtUtc, List<ResponderGroupOptionDto> Groups);
public record CreateResponderDto(
    string MobileNumber,
    string NationalCode,
    string Gender,
    string? FullName = null,
    string? FirstName = null,
    string? LastName = null,
    List<Guid>? GroupIds = null);
public record UpdateResponderDto(
    string MobileNumber,
    string NationalCode,
    string? FullName = null,
    string? FirstName = null,
    string? LastName = null,
    string? Gender = null,
    List<Guid>? GroupIds = null);
public record ImportRespondersDto(List<ImportResponderRowDto> Rows, List<Guid>? GroupIds = null);
public record ImportResponderRowDto(
    int RowNumber,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? NationalCode = null,
    string? Gender = null);
