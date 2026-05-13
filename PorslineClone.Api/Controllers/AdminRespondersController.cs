using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

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

    [HttpGet("export")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var query = db.Responders.AsQueryable();
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
                x.CreatedAtUtc
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("options")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Options([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var q = db.Responders.AsQueryable();
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue) return Ok(new List<object>());
            q = q.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.FullName.Contains(s) || x.MobileNumber.Contains(s));
        }
        var items = await q
            .OrderBy(x => x.FullName)
            .Take(50)
            .Select(x => new { x.Id, x.FullName, x.MobileNumber })
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
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Responders.AsQueryable();
        if (!CanReadAllResponders)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.FullName.Contains(q) || x.MobileNumber.Contains(q));
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
        var fullName = dto.FullName.Trim();
        var mobile = dto.MobileNumber.Trim();

        if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
        if (!IsValidMobile(mobile)) return BadRequest(new { message = "شماره موبایل معتبر نیست" });
        if (await db.Responders.AnyAsync(x => x.MobileNumber == mobile, ct))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        var entity = new Responder
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            MobileNumber = mobile,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow
        };
        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (groupIds.Count > 0)
        {
            var validGroups = await db.ResponderGroups.Where(x => groupIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            foreach (var gid in validGroups)
                entity.GroupMembers.Add(new ResponderGroupMember { ResponderId = entity.Id, GroupId = gid });
        }
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
        if (item is null) return NotFound(new { message = "پاسخگو یافت نشد" });

        var fullName = dto.FullName.Trim();
        var mobile = dto.MobileNumber.Trim();
        if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
        if (!IsValidMobile(mobile)) return BadRequest(new { message = "شماره موبایل معتبر نیست" });
        if (await db.Responders.AnyAsync(x => x.MobileNumber == mobile && x.Id != id, ct))
            return BadRequest(new { message = "این شماره موبایل قبلا ثبت شده است" });

        item.FullName = fullName;
        item.MobileNumber = mobile;
        var groupIds = (dto.GroupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        item.GroupMembers.Clear();
        if (groupIds.Count > 0)
        {
            var validGroups = await db.ResponderGroups.Where(x => groupIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            foreach (var gid in validGroups)
                item.GroupMembers.Add(new ResponderGroupMember { ResponderId = item.Id, GroupId = gid });
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخگو بروزرسانی شد" });
    }

    [HttpPost("import")]
    [Authorize(Policy = "responders.add")]
    public async Task<IActionResult> Import([FromBody] ImportRespondersDto dto, CancellationToken ct)
    {
        var invalidRows = new List<object>();
        var valid = new List<(string Name, string Mobile, int Row)>();

        foreach (var r in dto.Rows)
        {
            var name = (r.FullName ?? "").Trim();
            var mobile = (r.MobileNumber ?? "").Trim();
            if (name.Length < 2 || !IsValidMobile(mobile))
            {
                invalidRows.Add(new { r.RowNumber, r.FullName, r.MobileNumber, reason = "نام یا موبایل نامعتبر" });
                continue;
            }
            valid.Add((name, mobile, r.RowNumber));
        }

        var duplicateInFile = valid
            .GroupBy(x => x.Mobile)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(x => new { x.Row, x.Name, Mobile = x.Mobile, reason = "موبایل تکراری در فایل" }))
            .ToList();
        if (duplicateInFile.Count > 0)
            invalidRows.AddRange(duplicateInFile);

        var uniqueValid = valid
            .GroupBy(x => x.Mobile)
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .ToList();

        var mobiles = uniqueValid.Select(x => x.Mobile).ToList();
        var existing = await db.Responders.Where(x => mobiles.Contains(x.MobileNumber)).ToListAsync(ct);
        var existingMap = existing.ToDictionary(x => x.MobileNumber, x => x);

        var inserted = 0;
        var updated = 0;
        foreach (var v in uniqueValid)
        {
            if (existingMap.TryGetValue(v.Mobile, out var ex))
            {
                ex.FullName = v.Name;
                updated++;
            }
            else
            {
                db.Responders.Add(new Responder
                {
                    Id = Guid.NewGuid(),
                    FullName = v.Name,
                    MobileNumber = v.Mobile,
                    CreatedByUserId = CurrentUserGuid,
                    CreatedAtUtc = DateTime.UtcNow
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            message = "ایمپورت انجام شد",
            inserted,
            updated,
            invalidCount = invalidRows.Count,
            invalidRows
        });
    }

    private static bool IsValidMobile(string mobile) => System.Text.RegularExpressions.Regex.IsMatch(mobile, "^09\\d{9}$");
}

public record ResponderGroupOptionDto(Guid Id, string Name);
public record ResponderItemDto(Guid Id, string FullName, string MobileNumber, DateTime CreatedAtUtc, List<ResponderGroupOptionDto> Groups);
public record CreateResponderDto(string FullName, string MobileNumber, List<Guid>? GroupIds = null);
public record UpdateResponderDto(string FullName, string MobileNumber, List<Guid>? GroupIds = null);
public record ImportRespondersDto(List<ImportResponderRowDto> Rows);
public record ImportResponderRowDto(int RowNumber, string? FullName, string? MobileNumber);
