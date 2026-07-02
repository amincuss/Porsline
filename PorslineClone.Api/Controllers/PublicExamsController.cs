using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.ExamDispatch;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/exams")]
[AllowAnonymous]
public class PublicExamsController(AppDbContext db) : ControllerBase
{
    [HttpGet("access")]
    public async Task<IActionResult> Access([FromQuery] string c, CancellationToken ct)
    {
        var ctx = await LoadLinkContextAsync(c, ct);
        if (ctx.Error is not null) return ctx.Error;
        var link = ctx.Link!;
        var form = ctx.Form!;
        var existing = ctx.Existing;

        if (existing is not null || link!.UsedAtUtc is not null)
        {
            return Ok(new
            {
                alreadySubmitted = true,
                message = "این آزمون قبلاً ثبت شده است",
                submittedAtUtc = existing?.SubmittedAtUtc ?? link.UsedAtUtc,
                correctCount = existing?.CorrectCount,
                scorableQuestionCount = existing?.ScorableQuestionCount,
                passingCorrectCount = existing?.PassingCorrectCount,
                isPassed = existing?.IsPassed,
            });
        }

        var now = DateTime.UtcNow;
        var (windowStart, windowEnd) = EffectiveWindow(link, form);

        if (windowStart is not null && now < windowStart.Value)
        {
            return Ok(BuildWelcomeResponse(form, link, now, windowStart, windowEnd, canStart: false, notStarted: true));
        }

        if (windowEnd is not null && now >= windowEnd.Value)
        {
            await ExpireLinkAsync(link, ct);
            return Ok(new
            {
                expired = true,
                message = "مهلت آزمون به پایان رسیده است",
                windowEndAtUtc = windowEnd,
                serverNowUtc = now,
            });
        }

        if (link.StartedAtUtc is not null)
        {
            var expiresAtUtc = AsUtc(link.ExpiresAtUtc);
            if (expiresAtUtc is not null && now >= expiresAtUtc.Value)
            {
                await ExpireLinkAsync(link, ct);
                return Ok(new
                {
                    expired = true,
                    message = "زمان آزمون به پایان رسیده است",
                    expiresAtUtc = expiresAtUtc,
                    serverNowUtc = now,
                });
            }

            return Ok(BuildExamPayload(form!, link, now, includeQuestions: true));
        }

        return Ok(BuildWelcomeResponse(form!, link, now, windowStart, windowEnd, canStart: true, notStarted: false));
    }

    [HttpPost("access/start")]
    public async Task<IActionResult> Start([FromBody] PublicExamStartRequest req, CancellationToken ct)
    {
        var ctx = await LoadLinkContextAsync(req.Code, ct);
        if (ctx.Error is not null) return ctx.Error;
        var link = ctx.Link!;
        var form = ctx.Form!;
        var existing = ctx.Existing;

        if (existing is not null || link.UsedAtUtc is not null)
            return BadRequest(new { message = "این آزمون قبلاً ثبت شده است", alreadySubmitted = true });

        var now = DateTime.UtcNow;
        var (windowStart, windowEnd) = EffectiveWindow(link, form);

        if (windowStart is not null && now < windowStart.Value)
            return BadRequest(new { message = "آزمون هنوز شروع نشده است", notStarted = true });

        if (windowEnd is not null && now >= windowEnd.Value)
        {
            await ExpireLinkAsync(link, ct);
            return BadRequest(new { message = "مهلت آزمون به پایان رسیده است", expired = true });
        }

        if (link.StartedAtUtc is null)
        {
            link.StartedAtUtc = AsUtc(now);
            var sessionEnd = now.AddMinutes(Math.Max(1, form.DurationMinutes));
            var expiresAt = windowEnd is null || sessionEnd < windowEnd.Value
                ? sessionEnd
                : windowEnd.Value;
            link.ExpiresAtUtc = AsUtc(expiresAt);
            await db.SaveChangesAsync(ct);
        }

        var expiresAtUtc = AsUtc(link.ExpiresAtUtc);
        if (expiresAtUtc is not null && now >= expiresAtUtc.Value)
        {
            await ExpireLinkAsync(link, ct);
            return BadRequest(new { message = "زمان آزمون به پایان رسیده است", expired = true });
        }

        return Ok(BuildExamPayload(form!, link, now, includeQuestions: true));
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] PublicExamSubmitRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "کد لینک نامعتبر است" });

        var link = await db.ExamLinks
            .Include(x => x.ExamForm)
            .ThenInclude(f => f!.Questions)
            .Include(x => x.ExamDispatch)
            .FirstOrDefaultAsync(x => x.Code == req.Code.Trim(), ct);

        if (link is null || !link.IsActive)
            return BadRequest(new { message = "لینک آزمون نامعتبر است" });

        if (await db.ExamSubmissions.AnyAsync(x => x.ExamLinkId == link.Id, ct) || link.UsedAtUtc is not null)
            return Ok(new { alreadySubmitted = true, message = "این آزمون قبلاً ثبت شده است" });

        var form = link.ExamForm;
        if (form is null || !form.IsActive)
            return BadRequest(new { message = "این آزمون غیرفعال است" });

        var now = DateTime.UtcNow;
        var (windowStart, windowEnd) = EffectiveWindow(link, form);

        if (windowStart is not null && now < windowStart.Value)
            return BadRequest(new { message = "آزمون هنوز شروع نشده است", notStarted = true });

        if (windowEnd is not null && now >= windowEnd.Value)
            return BadRequest(new { message = "مهلت آزمون به پایان رسیده است", expired = true });

        if (link.StartedAtUtc is null)
            return BadRequest(new { message = "ابتدا دکمه شروع آزمون را بزنید" });

        var expiresAtUtc = AsUtc(link.ExpiresAtUtc);
        var expired = expiresAtUtc is not null && now >= expiresAtUtc.Value;
        var autoSubmitted = expired || req.AutoSubmitted;

        if (expired && !req.AutoSubmitted)
            return BadRequest(new { message = "زمان آزمون به پایان رسیده است", expired = true });

        var answers = req.Answers ?? new Dictionary<string, string>();
        foreach (var q in form.Questions.Where(x => x.IsRequired))
        {
            if (!answers.TryGetValue(q.Id.ToString(), out var v) || string.IsNullOrWhiteSpace(v))
            {
                if (!autoSubmitted)
                    return BadRequest(new { message = $"پاسخ سوال «{q.Label}» الزامی است" });
            }
        }

        var passingCorrectCount = link.ExamDispatch?.PassingCorrectCount;
        var score = ExamScoringHelper.Score(form, answers, passingCorrectCount);

        var submission = new ExamSubmission
        {
            Id = Guid.NewGuid(),
            ExamLinkId = link.Id,
            ExamFormId = form.Id,
            AnswersJson = JsonSerializer.Serialize(answers),
            CorrectCount = score.CorrectCount,
            ScorableQuestionCount = score.ScorableQuestionCount,
            PassingCorrectCount = passingCorrectCount,
            IsPassed = score.IsPassed,
            SubmittedAtUtc = DateTime.UtcNow,
            IsAutoSubmitted = autoSubmitted,
        };
        db.ExamSubmissions.Add(submission);
        link.UsedAtUtc = DateTime.UtcNow;
        link.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = autoSubmitted ? "زمان آزمون پایان یافت — پاسخ‌ها ثبت شد" : "آزمون با موفقیت ثبت شد",
            autoSubmitted,
            correctCount = score.CorrectCount,
            scorableQuestionCount = score.ScorableQuestionCount,
            passingCorrectCount,
            isPassed = score.IsPassed,
        });
    }

    private async Task<(ExamLink? Link, ExamForm? Form, ExamSubmission? Existing, IActionResult? Error)> LoadLinkContextAsync(
        string? code,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (null, null, null, BadRequest(new { message = "کد لینک نامعتبر است" }));

        var link = await db.ExamLinks
            .Include(x => x.ExamForm!)
            .ThenInclude(f => f.Questions)
            .FirstOrDefaultAsync(x => x.Code == code.Trim(), ct);

        if (link is null || !link.IsActive)
            return (null, null, null, BadRequest(new { message = "لینک آزمون نامعتبر است" }));

        var form = link.ExamForm;
        if (form is null || !form.IsActive)
            return (null, null, null, BadRequest(new { message = "این آزمون غیرفعال است" }));

        var existing = await db.ExamSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamLinkId == link.Id, ct);

        return (link, form, existing, null);
    }

    private static (DateTime? Start, DateTime? End) EffectiveWindow(ExamLink link, ExamForm form) =>
        (AsUtc(link.WindowStartAtUtc ?? form.WindowStartAtUtc), AsUtc(link.WindowEndAtUtc ?? form.WindowEndAtUtc));

    /// <summary>datetime2 از SQL بدون Kind می‌آید — همیشه UTC فرض می‌کنیم.</summary>
    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private async Task ExpireLinkAsync(ExamLink link, CancellationToken ct)
    {
        if (link.UsedAtUtc is not null) return;
        link.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private static object BuildWelcomeResponse(
        ExamForm form,
        ExamLink link,
        DateTime now,
        DateTime? windowStart,
        DateTime? windowEnd,
        bool canStart,
        bool notStarted)
    {
        return new
        {
            welcome = true,
            notStarted,
            canStart,
            message = notStarted ? "آزمون هنوز شروع نشده است" : null,
            form.Id,
            form.Title,
            form.Description,
            form.DurationMinutes,
            windowStartAtUtc = windowStart,
            windowEndAtUtc = windowEnd,
            serverNowUtc = AsUtc(now),
            participantName = link.ParticipantName,
        };
    }

    private static object BuildExamPayload(ExamForm form, ExamLink link, DateTime now, bool includeQuestions)
    {
        object questions = includeQuestions
            ? form.Questions.OrderBy(q => q.SortOrder).Select(q => new
            {
                q.Id,
                QuestionType = (int)q.QuestionType,
                q.Label,
                Options = DeserializeOptions(q),
                q.IsRequired,
            }).ToList()
            : [];

        return new
        {
            form.Id,
            form.Title,
            form.Description,
            form.DurationMinutes,
            windowStartAtUtc = AsUtc(link.WindowStartAtUtc ?? form.WindowStartAtUtc),
            windowEndAtUtc = AsUtc(link.WindowEndAtUtc ?? form.WindowEndAtUtc),
            startedAtUtc = AsUtc(link.StartedAtUtc),
            expiresAtUtc = AsUtc(link.ExpiresAtUtc),
            serverNowUtc = AsUtc(now),
            participantName = link.ParticipantName,
            Questions = questions,
        };
    }

    private static List<string> DeserializeOptions(ExamQuestion q)
    {
        if (string.IsNullOrWhiteSpace(q.OptionsJson)) return DefaultOptions(q.QuestionType);
        try
        {
            return JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? DefaultOptions(q.QuestionType);
        }
        catch
        {
            return DefaultOptions(q.QuestionType);
        }
    }

    private static List<string> DefaultOptions(ExamQuestionType type) => type switch
    {
        ExamQuestionType.TwoOption => ["گزینه ۱", "گزینه ۲"],
        ExamQuestionType.FourOption => ["گزینه ۱", "گزینه ۲", "گزینه ۳", "گزینه ۴"],
        _ => [],
    };
}

public class PublicExamStartRequest
{
    public string Code { get; set; } = "";
}

public class PublicExamSubmitRequest
{
    public string Code { get; set; } = "";
    public Dictionary<string, string>? Answers { get; set; }
    public bool AutoSubmitted { get; set; }
}
