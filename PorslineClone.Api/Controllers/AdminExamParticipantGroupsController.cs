using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/exam-participant-groups")]
[Authorize]
public class AdminExamParticipantGroupsController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserGuid =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    private bool CanReadAll => User.HasClaim("permission", "exams.read.all");

    private IActionResult? DenyUnlessCanModify(ExamParticipantGroup item)
    {
        if (CanReadAll) return null;
        var uid = CurrentUserGuid;
        if (!uid.HasValue || item.CreatedByUserId != uid.Value)
            return Forbid();
        return null;
    }

    [HttpGet]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.ExamParticipantGroups.AsQueryable();
        if (!CanReadAll)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.Name.Contains(q));
        }
        query = sortBy switch
        {
            "name_asc" => query.OrderBy(x => x.Name),
            "name_desc" => query.OrderByDescending(x => x.Name),
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
                x.Name,
                x.IsActive,
                x.CreatedAtUtc,
                MemberCount = x.Members.Count,
            })
            .ToListAsync(ct);
        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpPost]
    [Authorize(Policy = "exams.add")]
    public async Task<IActionResult> Create([FromBody] ExamParticipantGroupUpsertDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ExamParticipantGroups.AnyAsync(x => x.Name == name, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        db.ExamParticipantGroups.Add(new ExamParticipantGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = dto.IsActive,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ExamParticipantGroupUpsertDto dto, CancellationToken ct)
    {
        var item = await db.ExamParticipantGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound(new { message = "گروه یافت نشد" });
        var denied = DenyUnlessCanModify(item);
        if (denied is not null) return denied;
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ExamParticipantGroups.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        item.Name = name;
        item.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "exams.delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var item = await db.ExamParticipantGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound(new { message = "گروه یافت نشد" });
        var denied = DenyUnlessCanModify(item);
        if (denied is not null) return denied;
        item.IsDeleted = true;
        item.IsActive = false;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه حذف شد" });
    }

    [HttpDelete("{groupId:guid}/members/{participantId:guid}")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> RemoveMember(Guid groupId, Guid participantId, CancellationToken ct)
    {
        var group = await db.ExamParticipantGroups.FirstOrDefaultAsync(x => x.Id == groupId, ct);
        if (group is null) return NotFound(new { message = "گروه یافت نشد" });
        var denied = DenyUnlessCanModify(group);
        if (denied is not null) return denied;

        var member = await db.ExamParticipantGroupMembers
            .FirstOrDefaultAsync(x => x.GroupId == groupId && x.ParticipantId == participantId, ct);
        if (member is null)
            return NotFound(new { message = "این آزمون‌دهنده عضو این گروه نیست" });

        db.ExamParticipantGroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "آزمون‌دهنده از گروه حذف شد" });
    }
}

public record ExamParticipantGroupUpsertDto(string Name, bool IsActive = true);
