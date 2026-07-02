using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.ExamDispatch;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/exams/send")]
[Authorize]
public class AdminExamDispatchController(
    AppDbContext db,
    ExamDispatchGroupSendService dispatchService) : ControllerBase
{
    private Guid? CurrentUserGuid =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    [HttpGet("groups")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Groups(CancellationToken ct)
    {
        var query = db.ExamParticipantGroups.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive);
        if (!User.HasClaim("permission", "exams.read.all"))
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue) return Ok(Array.Empty<object>());
            query = query.Where(x => x.CreatedByUserId == uid.Value);
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                MemberCount = x.Members.Count,
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("exams")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Exams([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var query = db.ExamForms.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.Title.Contains(q));
        }

        var items = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Description,
                x.DurationMinutes,
                x.WindowStartAtUtc,
                x.WindowEndAtUtc,
                QuestionCount = x.Questions.Count,
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("preview")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> Preview([FromBody] ExamDispatchPreviewRequest req, CancellationToken ct)
    {
        var groupIds = (req.GroupIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (req.ExamFormId == Guid.Empty)
            return BadRequest(new { message = "فرم آزمون را انتخاب کنید" });
        if (groupIds.Count == 0)
            return BadRequest(new { message = "حداقل یک گروه انتخاب کنید" });

        var preview = await dispatchService.PreviewAsync(req.ExamFormId, groupIds, ct);
        if (preview is null)
            return NotFound(new { message = "فرم آزمون یا گروه‌ها یافت نشد" });
        return Ok(preview);
    }

    [HttpPost]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> Send([FromBody] ExamDispatchSendRequest req, CancellationToken ct)
    {
        var groupIds = (req.GroupIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (req.ExamFormId == Guid.Empty)
            return BadRequest(new { message = "فرم آزمون را انتخاب کنید" });
        if (groupIds.Count == 0)
            return BadRequest(new { message = "حداقل یک گروه انتخاب کنید" });
        if (req.WindowStartAtUtc == default || req.WindowEndAtUtc == default)
            return BadRequest(new { message = "زمان شروع و پایان آزمون را مشخص کنید" });

        try
        {
            var result = await dispatchService.SendAsync(
                req.ExamFormId,
                groupIds,
                req.WindowStartAtUtc,
                req.WindowEndAtUtc,
                req.PassingCorrectCount,
                CurrentUserGuid,
                req.ExamTitle,
                ct);
            return Ok(new
            {
                message = $"ارسال انجام شد — {result.SentCount} موفق، {result.FailedCount} ناموفق",
                result.DispatchId,
                result.TotalParticipants,
                result.SentCount,
                result.FailedCount,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class ExamDispatchPreviewRequest
{
    public Guid ExamFormId { get; set; }
    public List<Guid>? GroupIds { get; set; }
}

public class ExamDispatchSendRequest
{
    public Guid ExamFormId { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public DateTime WindowStartAtUtc { get; set; }
    public DateTime WindowEndAtUtc { get; set; }
    public int PassingCorrectCount { get; set; } = 1;
    public string? ExamTitle { get; set; }
}
