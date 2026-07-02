using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/exams")]
[Authorize]
public class AdminExamsController(
    AppDbContext db,
    IFrontendUrlResolver frontendUrls) : ControllerBase
{
    private Guid? CurrentUserGuid =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await db.ExamForms.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Description,
                x.DurationMinutes,
                x.IsActive,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                QuestionCount = x.Questions.Count,
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "exams.add")]
    public async Task<IActionResult> Create([FromBody] CreateExamRequest? req, CancellationToken ct)
    {
        var form = new ExamForm
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(req?.Title) ? "آزمون بدون عنوان" : req!.Title!.Trim(),
            Description = req?.Description?.Trim(),
            DurationMinutes = req?.DurationMinutes is > 0 and <= 600 ? req.DurationMinutes.Value : 60,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.ExamForms.Add(form);
        await db.SaveChangesAsync(ct);
        return Ok(new { form.Id, form.Title });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var form = await db.ExamForms.AsNoTracking()
            .Include(x => x.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (form is null) return NotFound(new { message = "آزمون یافت نشد" });
        return Ok(MapExamDetail(form));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveExamRequest req, CancellationToken ct)
    {
        var form = await db.ExamForms
            .Include(x => x.Questions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (form is null) return NotFound(new { message = "آزمون یافت نشد" });

        form.Title = string.IsNullOrWhiteSpace(req.Title) ? form.Title : req.Title.Trim();
        form.Description = req.Description?.Trim();
        if (req.DurationMinutes is > 0 and <= 600)
            form.DurationMinutes = req.DurationMinutes.Value;
        if (req.IsActive.HasValue)
            form.IsActive = req.IsActive.Value;

        form.WindowStartAtUtc = req.WindowStartAtUtc.HasValue
            ? DateTime.SpecifyKind(req.WindowStartAtUtc.Value, DateTimeKind.Utc)
            : null;
        form.WindowEndAtUtc = req.WindowEndAtUtc.HasValue
            ? DateTime.SpecifyKind(req.WindowEndAtUtc.Value, DateTimeKind.Utc)
            : null;

        if (form.WindowStartAtUtc is not null && form.WindowEndAtUtc is not null
            && form.WindowStartAtUtc >= form.WindowEndAtUtc)
            return BadRequest(new { message = "تاریخ و ساعت شروع باید قبل از پایان باشد" });

        form.UpdatedAtUtc = DateTime.UtcNow;

        var existing = form.Questions.ToDictionary(x => x.Id);
        var kept = new HashSet<Guid>();
        var sort = 0;
        foreach (var q in req.Questions ?? [])
        {
            var qid = q.Id is Guid g && g != Guid.Empty ? g : Guid.NewGuid();
            kept.Add(qid);
            var optionsJson = q.Options is { Count: > 0 }
                ? JsonSerializer.Serialize(q.Options)
                : null;
            if (existing.TryGetValue(qid, out var row))
            {
                row.QuestionType = ParseQuestionType(q.QuestionType);
                row.Label = string.IsNullOrWhiteSpace(q.Label) ? "سوال" : q.Label.Trim();
                row.OptionsJson = optionsJson;
                row.IsRequired = q.IsRequired;
                row.CorrectAnswerIndex = q.CorrectAnswerIndex;
                row.SortOrder = sort++;
            }
            else
            {
                db.ExamQuestions.Add(new ExamQuestion
                {
                    Id = qid,
                    ExamFormId = form.Id,
                    QuestionType = ParseQuestionType(q.QuestionType),
                    Label = string.IsNullOrWhiteSpace(q.Label) ? "سوال" : q.Label.Trim(),
                    OptionsJson = optionsJson,
                    IsRequired = q.IsRequired,
                    CorrectAnswerIndex = q.CorrectAnswerIndex,
                    SortOrder = sort++,
                });
            }
        }

        foreach (var old in form.Questions.Where(x => !kept.Contains(x.Id)).ToList())
            db.ExamQuestions.Remove(old);

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "آزمون ذخیره شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "exams.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var form = await db.ExamForms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (form is null) return NotFound(new { message = "آزمون یافت نشد" });
        form.IsDeleted = true;
        form.DeletedAtUtc = DateTime.UtcNow;
        form.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "آزمون حذف شد" });
    }

    [HttpPost("{id:guid}/links")]
    [Authorize(Policy = "exams.update")]
    public async Task<IActionResult> CreateLink(Guid id, [FromBody] CreateExamLinkRequest? req, CancellationToken ct)
    {
        var form = await db.ExamForms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (form is null) return NotFound(new { message = "آزمون فعال یافت نشد" });

        var code = await ExamLinkCodeGenerator.GenerateUniqueAsync(db, ct);
        var link = new ExamLink
        {
            Id = Guid.NewGuid(),
            ExamFormId = id,
            Code = code,
            ParticipantName = req?.ParticipantName?.Trim(),
            ParticipantMobile = req?.ParticipantMobile?.Trim(),
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.ExamLinks.Add(link);
        await db.SaveChangesAsync(ct);

        var baseUrl = (await frontendUrls.ResolvePublicBaseUrlAsync(ct))?.TrimEnd('/') ?? "";
        var path = $"/exams/fill?c={code}";
        var url = string.IsNullOrEmpty(baseUrl) ? path : $"{baseUrl}{path}";
        return Ok(new { link.Id, link.Code, url, message = "لینک آزمون ساخته شد" });
    }

    private static ExamQuestionType ParseQuestionType(int raw) => raw switch
    {
        2 => ExamQuestionType.TwoOption,
        3 => ExamQuestionType.Descriptive,
        _ => ExamQuestionType.FourOption,
    };

    private static object MapExamDetail(ExamForm form) => new
    {
        form.Id,
        form.Title,
        form.Description,
        form.DurationMinutes,
        form.WindowStartAtUtc,
        form.WindowEndAtUtc,
        form.IsActive,
        form.CreatedAtUtc,
        form.UpdatedAtUtc,
        serverNowUtc = DateTime.UtcNow,
        Questions = form.Questions.OrderBy(q => q.SortOrder).Select(q => new
        {
            q.Id,
            QuestionType = (int)q.QuestionType,
            q.Label,
            Options = string.IsNullOrWhiteSpace(q.OptionsJson)
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? [],
            q.IsRequired,
            q.CorrectAnswerIndex,
            q.SortOrder,
        }),
    };
}

public class CreateExamRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? DurationMinutes { get; set; }
}

public class SaveExamRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? DurationMinutes { get; set; }
    public DateTime? WindowStartAtUtc { get; set; }
    public DateTime? WindowEndAtUtc { get; set; }
    public bool? IsActive { get; set; }
    public List<SaveExamQuestionRequest>? Questions { get; set; }
}

public class SaveExamQuestionRequest
{
    public Guid? Id { get; set; }
    public int QuestionType { get; set; } = 1;
    public string Label { get; set; } = "";
    public List<string>? Options { get; set; }
    public int? CorrectAnswerIndex { get; set; }
    public bool IsRequired { get; set; } = true;
}

public class CreateExamLinkRequest
{
    public string? ParticipantName { get; set; }
    public string? ParticipantMobile { get; set; }
}
