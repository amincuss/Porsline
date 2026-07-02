using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.ExamDispatch;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/exams/results")]
[Authorize]
public class AdminExamResultsController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserGuid =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    private bool CanReadAll => User.HasClaim("permission", "exams.read.all");

    [HttpGet("exams")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Exams([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var dispatchQuery = db.ExamDispatches.AsNoTracking().AsQueryable();
        if (!CanReadAll)
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue) return Ok(Array.Empty<object>());
            dispatchQuery = dispatchQuery.Where(x => x.CreatedByUserId == uid.Value);
        }

        var dispatchRows = await (
            from d in dispatchQuery
            join f in db.ExamForms.AsNoTracking() on d.ExamFormId equals f.Id
            where !f.IsDeleted
            select new
            {
                d.ExamFormId,
                f.Title,
                d.GroupIdsJson,
                d.CreatedAtUtc,
            }
        ).ToListAsync(ct);

        if (dispatchRows.Count == 0) return Ok(Array.Empty<object>());

        var examMeta = dispatchRows
            .GroupBy(x => x.ExamFormId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(x => x.CreatedAtUtc).First();
                    return new
                    {
                        latest.Title,
                        GroupIds = g.SelectMany(x => ParseGroupIds(x.GroupIdsJson)).Distinct().ToList(),
                        DispatchCount = g.Count(),
                        LastDispatchAtUtc = g.Max(x => x.CreatedAtUtc),
                    };
                });

        var allGroupIds = examMeta.Values.SelectMany(x => x.GroupIds).Distinct().ToList();
        var memberRows = allGroupIds.Count == 0
            ? []
            : await (
                from m in db.ExamParticipantGroupMembers.AsNoTracking()
                where allGroupIds.Contains(m.GroupId)
                join p in db.ExamParticipants.AsNoTracking() on m.ParticipantId equals p.Id
                where !p.IsDeleted
                join gr in db.ExamParticipantGroups.AsNoTracking() on m.GroupId equals gr.Id
                select new
                {
                    m.GroupId,
                    GroupName = gr.Name,
                    p.Id,
                }
            ).ToListAsync(ct);

        var examFormIds = examMeta.Keys.ToList();
        var participantIds = memberRows.Select(x => x.Id).Distinct().ToList();
        var submissionRows = participantIds.Count == 0
            ? []
            : await (
                from l in db.ExamLinks.AsNoTracking()
                where l.ExamParticipantId != null
                    && participantIds.Contains(l.ExamParticipantId.Value)
                    && examFormIds.Contains(l.ExamFormId)
                join s in db.ExamSubmissions.AsNoTracking() on l.Id equals s.ExamLinkId
                select new
                {
                    l.ExamFormId,
                    ParticipantId = l.ExamParticipantId!.Value,
                    s.IsPassed,
                }
            ).ToListAsync(ct);

        var membersByGroup = memberRows.GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToHashSet());
        var submissionsByExam = submissionRows.GroupBy(x => x.ExamFormId).ToDictionary(g => g.Key, g => g.ToList());

        var items = examMeta.Select(kv =>
        {
            var examFormId = kv.Key;
            var meta = kv.Value;
            var assignedIds = meta.GroupIds
                .SelectMany(gid => membersByGroup.GetValueOrDefault(gid) ?? [])
                .ToHashSet();
            var subs = submissionsByExam.GetValueOrDefault(examFormId) ?? [];
            var takenIds = subs.Select(x => x.ParticipantId).Distinct().ToHashSet();
            var passedIds = subs.Where(x => x.IsPassed == true).Select(x => x.ParticipantId).Distinct().ToHashSet();
            var failedIds = takenIds.Where(id => !passedIds.Contains(id)).ToHashSet();

            return new
            {
                Id = examFormId,
                meta.Title,
                meta.DispatchCount,
                meta.LastDispatchAtUtc,
                AssignedCount = assignedIds.Count,
                CompletedCount = takenIds.Count,
                PassedCount = passedIds.Count,
                FailedCount = failedIds.Count,
                NotTakenCount = assignedIds.Count - takenIds.Count,
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            items = items.Where(x => x.Title.Contains(q, StringComparison.Ordinal));
        }

        return Ok(items.OrderByDescending(x => x.LastDispatchAtUtc).ThenBy(x => x.Title));
    }

    [HttpGet("exams/{examFormId:guid}/participants")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> ExamParticipants(
        Guid examFormId,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? result = null,
        CancellationToken ct = default)
    {
        var form = await db.ExamForms.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == examFormId && !x.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "آزمون یافت نشد" });

        var dispatchQuery = db.ExamDispatches.AsNoTracking()
            .Where(x => x.ExamFormId == examFormId);
        if (!CanReadAll)
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue) return Forbid();
            dispatchQuery = dispatchQuery.Where(x => x.CreatedByUserId == uid.Value);
        }

        var dispatches = await dispatchQuery.Select(x => new { x.GroupIdsJson }).ToListAsync(ct);
        if (dispatches.Count == 0)
            return NotFound(new { message = "این آزمون هنوز برای گروهی ارسال نشده است" });

        var groupIds = dispatches.SelectMany(x => ParseGroupIds(x.GroupIdsJson)).Distinct().ToList();
        if (groupIds.Count == 0)
            return Ok(new { examTitle = form.Title, summary = EmptySummary(), items = Array.Empty<object>() });

        var members = await (
            from m in db.ExamParticipantGroupMembers.AsNoTracking()
            where groupIds.Contains(m.GroupId)
            join p in db.ExamParticipants.AsNoTracking() on m.ParticipantId equals p.Id
            where !p.IsDeleted
            join gr in db.ExamParticipantGroups.AsNoTracking() on m.GroupId equals gr.Id
            select new
            {
                p.Id,
                p.FirstName,
                p.LastName,
                p.MobileNumber,
                GroupName = gr.Name,
            }
        ).ToListAsync(ct);

        var memberIds = members.Select(x => x.Id).Distinct().ToList();
        var submissionRows = memberIds.Count == 0
            ? []
            : await (
                from l in db.ExamLinks.AsNoTracking()
                where l.ExamFormId == examFormId
                    && l.ExamParticipantId != null
                    && memberIds.Contains(l.ExamParticipantId.Value)
                join s in db.ExamSubmissions.AsNoTracking() on l.Id equals s.ExamLinkId
                orderby s.SubmittedAtUtc descending
                select new
                {
                    ParticipantId = l.ExamParticipantId!.Value,
                    SubmissionId = s.Id,
                    s.SubmittedAtUtc,
                    s.IsPassed,
                    s.CorrectCount,
                    s.ScorableQuestionCount,
                    s.PassingCorrectCount,
                    s.IsAutoSubmitted,
                }
            ).ToListAsync(ct);

        var submissionByParticipant = submissionRows
            .GroupBy(x => x.ParticipantId)
            .ToDictionary(g => g.Key, g => g.First());

        var groupsByParticipant = members
            .GroupBy(x => x.Id)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.GroupName).Distinct().OrderBy(x => x).ToList());

        var memberInfo = members
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = memberInfo.Values.Select(m =>
        {
            submissionByParticipant.TryGetValue(m.Id, out var sub);
            groupsByParticipant.TryGetValue(m.Id, out var groupNames);
            groupNames ??= [];
            var submissions = sub is null
                ? []
                : new List<ParticipantSubmissionRow>
                {
                    new()
                    {
                        Id = sub.SubmissionId,
                        ExamTitle = form.Title,
                        SubmittedAtUtc = sub.SubmittedAtUtc,
                        IsPassed = sub.IsPassed,
                        CorrectCount = sub.CorrectCount ?? 0,
                        ScorableQuestionCount = sub.ScorableQuestionCount ?? 0,
                        PassingCorrectCount = sub.PassingCorrectCount ?? 0,
                        IsAutoSubmitted = sub.IsAutoSubmitted,
                    },
                };
            return new ExamParticipantResultRow
            {
                ParticipantId = m.Id,
                FirstName = m.FirstName,
                LastName = m.LastName,
                MobileNumber = m.MobileNumber,
                GroupNames = groupNames,
                ExamsTaken = submissions.Count,
                PassedCount = submissions.Count(x => x.IsPassed == true),
                FailedCount = submissions.Count(x => x.IsPassed == false),
                Submissions = submissions,
            };
        }).ToList();

        rows = ApplyParticipantFilters(rows, search, result);
        rows = ApplyParticipantSort(rows, sortBy);

        var allParticipantCount = memberInfo.Count;
        var takenIds = submissionByParticipant.Keys.ToHashSet();
        var passedParticipantCount = submissionRows.Where(x => x.IsPassed == true).Select(x => x.ParticipantId).Distinct().Count();
        var failedParticipantCount = takenIds.Count(id =>
            submissionByParticipant.TryGetValue(id, out var s) && s.IsPassed != true);

        return Ok(new
        {
            examTitle = form.Title,
            summary = new
            {
                participantCount = allParticipantCount,
                completedCount = takenIds.Count,
                passedCount = submissionRows.Count(x => x.IsPassed == true),
                failedCount = submissionRows.Count(x => x.IsPassed == false),
                notTakenCount = allParticipantCount - takenIds.Count,
                passedParticipantCount,
                failedParticipantCount,
            },
            items = rows,
        });
    }

    private static object EmptySummary() => new
    {
        participantCount = 0,
        completedCount = 0,
        passedCount = 0,
        failedCount = 0,
        notTakenCount = 0,
        passedParticipantCount = 0,
        failedParticipantCount = 0,
    };

    private static List<ExamParticipantResultRow> ApplyParticipantFilters(
        List<ExamParticipantResultRow> rows,
        string? search,
        string? result)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(x =>
                ($"{x.FirstName} {x.LastName}").Contains(q, StringComparison.Ordinal) ||
                x.MobileNumber.Contains(q, StringComparison.Ordinal) ||
                x.GroupNames.Any(g => g.Contains(q, StringComparison.Ordinal))).ToList();
        }

        return (result ?? "all").Trim().ToLowerInvariant() switch
        {
            "passed" => rows.Where(x => x.PassedCount > 0).ToList(),
            "failed" => rows.Where(x => x.ExamsTaken > 0 && x.PassedCount == 0).ToList(),
            "not_taken" => rows.Where(x => x.ExamsTaken == 0).ToList(),
            "taken" => rows.Where(x => x.ExamsTaken > 0).ToList(),
            _ => rows,
        };
    }

    private static List<ExamParticipantResultRow> ApplyParticipantSort(
        List<ExamParticipantResultRow> rows,
        string? sortBy) =>
        sortBy switch
        {
            "name_desc" => rows.OrderByDescending(x => x.LastName).ThenByDescending(x => x.FirstName).ToList(),
            "submitted_newest" => rows.OrderByDescending(x => x.Submissions.FirstOrDefault()?.SubmittedAtUtc ?? DateTime.MinValue).ToList(),
            "submitted_oldest" => rows.OrderBy(x => x.Submissions.FirstOrDefault()?.SubmittedAtUtc ?? DateTime.MaxValue).ToList(),
            "passed_first" => rows.OrderByDescending(x => x.PassedCount > 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            "failed_first" => rows.OrderByDescending(x => x.ExamsTaken > 0 && x.PassedCount == 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            "not_taken_first" => rows.OrderBy(x => x.ExamsTaken > 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            _ => rows.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
        };

    private static List<Guid> ParseGroupIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json)?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    [HttpGet("groups")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> Groups([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var query = db.ExamParticipantGroups.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive);
        if (!CanReadAll)
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue) return Ok(Array.Empty<object>());
            query = query.Where(x => x.CreatedByUserId == uid.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.Name.Contains(q));
        }

        var groups = await query.OrderBy(x => x.Name).ToListAsync(ct);
        var groupIds = groups.Select(x => x.Id).ToList();
        if (groupIds.Count == 0) return Ok(Array.Empty<object>());

        var memberCounts = await db.ExamParticipantGroupMembers.AsNoTracking()
            .Where(x => groupIds.Contains(x.GroupId))
            .GroupBy(x => x.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        var statsRows = await (
            from m in db.ExamParticipantGroupMembers.AsNoTracking()
            where groupIds.Contains(m.GroupId)
            join l in db.ExamLinks.AsNoTracking() on m.ParticipantId equals l.ExamParticipantId
            join s in db.ExamSubmissions.AsNoTracking() on l.Id equals s.ExamLinkId
            select new { m.GroupId, s.IsPassed }
        ).ToListAsync(ct);

        var statsByGroup = statsRows
            .GroupBy(x => x.GroupId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    CompletedCount = g.Count(),
                    PassedCount = g.Count(x => x.IsPassed == true),
                    FailedCount = g.Count(x => x.IsPassed == false),
                });

        var items = groups.Select(g =>
        {
            statsByGroup.TryGetValue(g.Id, out var stats);
            memberCounts.TryGetValue(g.Id, out var memberCount);
            return new
            {
                g.Id,
                g.Name,
                MemberCount = memberCount,
                CompletedCount = stats?.CompletedCount ?? 0,
                PassedCount = stats?.PassedCount ?? 0,
                FailedCount = stats?.FailedCount ?? 0,
            };
        });

        return Ok(items);
    }

    [HttpGet("groups/{groupId:guid}/participants")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> GroupParticipants(
        Guid groupId,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? result = null,
        CancellationToken ct = default)
    {
        var group = await db.ExamParticipantGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && !x.IsDeleted, ct);
        if (group is null) return NotFound(new { message = "گروه یافت نشد" });
        if (!CanReadAll)
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue || group.CreatedByUserId != uid.Value)
                return Forbid();
        }

        var members = await (
            from m in db.ExamParticipantGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join p in db.ExamParticipants.AsNoTracking() on m.ParticipantId equals p.Id
            where !p.IsDeleted
            select new
            {
                p.Id,
                p.FirstName,
                p.LastName,
                p.MobileNumber,
            }
        ).ToListAsync(ct);

        var memberIds = members.Select(x => x.Id).ToList();
        var submissionRows = memberIds.Count == 0
            ? []
            : await (
                from l in db.ExamLinks.AsNoTracking()
                where l.ExamParticipantId != null && memberIds.Contains(l.ExamParticipantId.Value)
                join s in db.ExamSubmissions.AsNoTracking() on l.Id equals s.ExamLinkId
                join f in db.ExamForms.AsNoTracking() on s.ExamFormId equals f.Id
                orderby s.SubmittedAtUtc descending
                select new
                {
                    ParticipantId = l.ExamParticipantId!.Value,
                    SubmissionId = s.Id,
                    s.SubmittedAtUtc,
                    s.IsPassed,
                    s.CorrectCount,
                    s.ScorableQuestionCount,
                    s.PassingCorrectCount,
                    s.IsAutoSubmitted,
                    ExamTitle = f.Title,
                }
            ).ToListAsync(ct);

        var submissionsByParticipant = submissionRows
            .GroupBy(x => x.ParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = members.Select(m =>
        {
            submissionsByParticipant.TryGetValue(m.Id, out var subs);
            subs ??= [];
            var passedCount = subs.Count(x => x.IsPassed == true);
            var failedCount = subs.Count(x => x.IsPassed == false);
            return new ParticipantResultRow
            {
                ParticipantId = m.Id,
                FirstName = m.FirstName,
                LastName = m.LastName,
                MobileNumber = m.MobileNumber,
                ExamsTaken = subs.Count,
                PassedCount = passedCount,
                FailedCount = failedCount,
                Submissions = subs.Select(s => new ParticipantSubmissionRow
                {
                    Id = s.SubmissionId,
                    ExamTitle = s.ExamTitle,
                    SubmittedAtUtc = s.SubmittedAtUtc,
                    IsPassed = s.IsPassed,
                    CorrectCount = s.CorrectCount ?? 0,
                    ScorableQuestionCount = s.ScorableQuestionCount ?? 0,
                    PassingCorrectCount = s.PassingCorrectCount ?? 0,
                    IsAutoSubmitted = s.IsAutoSubmitted,
                }).ToList(),
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(x =>
                ($"{x.FirstName} {x.LastName}").Contains(q, StringComparison.Ordinal) ||
                x.MobileNumber.Contains(q, StringComparison.Ordinal)).ToList();
        }

        rows = (result ?? "all").Trim().ToLowerInvariant() switch
        {
            "passed" => rows.Where(x => x.PassedCount > 0).ToList(),
            "failed" => rows.Where(x => x.ExamsTaken > 0 && x.PassedCount == 0).ToList(),
            "not_taken" => rows.Where(x => x.ExamsTaken == 0).ToList(),
            "taken" => rows.Where(x => x.ExamsTaken > 0).ToList(),
            _ => rows,
        };

        rows = sortBy switch
        {
            "name_desc" => rows.OrderByDescending(x => x.LastName).ThenByDescending(x => x.FirstName).ToList(),
            "submitted_newest" => rows.OrderByDescending(x => x.Submissions.FirstOrDefault()?.SubmittedAtUtc ?? DateTime.MinValue).ToList(),
            "submitted_oldest" => rows.OrderBy(x => x.Submissions.LastOrDefault()?.SubmittedAtUtc ?? DateTime.MaxValue).ToList(),
            "passed_first" => rows.OrderByDescending(x => x.PassedCount > 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            "failed_first" => rows.OrderByDescending(x => x.ExamsTaken > 0 && x.PassedCount == 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            "not_taken_first" => rows.OrderBy(x => x.ExamsTaken > 0).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
            _ => rows.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList(),
        };

        var participantCount = members.Count;
        var takenIds = submissionRows.Select(x => x.ParticipantId).Distinct().ToHashSet();
        var completedCount = takenIds.Count;
        var notTakenCount = participantCount - completedCount;
        var passedParticipantCount = submissionRows
            .GroupBy(x => x.ParticipantId)
            .Count(g => g.Any(x => x.IsPassed == true));
        var failedParticipantCount = takenIds.Count(id =>
            submissionRows.Where(x => x.ParticipantId == id).All(x => x.IsPassed != true));

        return Ok(new
        {
            summary = new
            {
                participantCount,
                completedCount,
                passedCount = submissionRows.Count(x => x.IsPassed == true),
                failedCount = submissionRows.Count(x => x.IsPassed == false),
                notTakenCount,
                passedParticipantCount,
                failedParticipantCount,
            },
            items = rows,
        });
    }

    [HttpGet("submissions/{submissionId:guid}")]
    [Authorize(Policy = "exams.read")]
    public async Task<IActionResult> SubmissionDetail(Guid submissionId, CancellationToken ct)
    {
        var submission = await db.ExamSubmissions.AsNoTracking()
            .Include(x => x.ExamLink)
            .Include(x => x.ExamForm)
            .ThenInclude(f => f!.Questions)
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission?.ExamForm is null) return NotFound(new { message = "ثبت آزمون یافت نشد" });

        if (!CanReadAll)
        {
            var uid = CurrentUserGuid;
            if (!uid.HasValue) return Forbid();
            var participantId = submission.ExamLink?.ExamParticipantId;
            if (participantId is null) return Forbid();

            var inOwnedGroup = await (
                from m in db.ExamParticipantGroupMembers.AsNoTracking()
                where m.ParticipantId == participantId.Value
                join g in db.ExamParticipantGroups.AsNoTracking() on m.GroupId equals g.Id
                where !g.IsDeleted && g.CreatedByUserId == uid.Value
                select m.ParticipantId
            ).AnyAsync(ct);
            if (!inOwnedGroup) return Forbid();
        }

        var answers = DeserializeAnswers(submission.AnswersJson);
        var questions = submission.ExamForm.Questions
            .OrderBy(q => q.SortOrder)
            .Select(q =>
            {
                answers.TryGetValue(q.Id.ToString(), out var userAnswer);
                userAnswer ??= "";
                return new
                {
                    q.Id,
                    q.QuestionType,
                    q.Label,
                    Options = DeserializeOptions(q),
                    UserAnswer = userAnswer,
                    CorrectAnswer = ExamScoringHelper.GetCorrectAnswerText(q),
                    IsCorrect = ExamScoringHelper.IsAnswerCorrect(q, userAnswer),
                    q.IsRequired,
                };
            })
            .ToList();

        return Ok(new
        {
            submission.Id,
            submission.SubmittedAtUtc,
            submission.IsAutoSubmitted,
            CorrectCount = submission.CorrectCount ?? 0,
            ScorableQuestionCount = submission.ScorableQuestionCount ?? 0,
            PassingCorrectCount = submission.PassingCorrectCount ?? 0,
            submission.IsPassed,
            ExamTitle = submission.ExamForm.Title,
            ParticipantName = submission.ExamLink?.ParticipantName,
            ParticipantMobile = submission.ExamLink?.ParticipantMobile,
            Questions = questions,
        });
    }

    private static Dictionary<string, string> DeserializeAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static List<string> DeserializeOptions(ExamQuestion q)
    {
        if (string.IsNullOrWhiteSpace(q.OptionsJson))
            return DefaultOptions(q.QuestionType);
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

    private sealed class ExamParticipantResultRow
    {
        public Guid ParticipantId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string MobileNumber { get; init; } = "";
        public List<string> GroupNames { get; init; } = [];
        public int ExamsTaken { get; init; }
        public int PassedCount { get; init; }
        public int FailedCount { get; init; }
        public List<ParticipantSubmissionRow> Submissions { get; init; } = [];
    }

    private sealed class ParticipantResultRow
    {
        public Guid ParticipantId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string MobileNumber { get; init; } = "";
        public int ExamsTaken { get; init; }
        public int PassedCount { get; init; }
        public int FailedCount { get; init; }
        public List<ParticipantSubmissionRow> Submissions { get; init; } = [];
    }

    private sealed class ParticipantSubmissionRow
    {
        public Guid Id { get; init; }
        public string ExamTitle { get; init; } = "";
        public DateTime SubmittedAtUtc { get; init; }
        public bool? IsPassed { get; init; }
        public int CorrectCount { get; init; }
        public int ScorableQuestionCount { get; init; }
        public int PassingCorrectCount { get; init; }
        public bool IsAutoSubmitted { get; init; }
    }
}
