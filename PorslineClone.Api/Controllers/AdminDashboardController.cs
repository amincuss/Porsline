using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize]
public partial class AdminDashboardController(AppDbContext db, UserManager<AppUser> userManager) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("feed")]
    public async Task<IActionResult> Feed(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var canContracts = User.HasClaim("permission", "contracts.read")
            || User.HasClaim("permission", "contracts.read.all");
        var canForms = User.HasClaim("permission", "forms.read")
            || User.HasClaim("permission", "forms.read.all");
        var canApprovals = User.HasClaim("permission", "approvals.read");

        var contracts = canContracts
            ? await BuildContractFeedAsync(userId, ct)
            : Array.Empty<DashboardFeedItemDto>();

        var forms = canForms
            ? await BuildFormSubmissionFeedAsync(ct)
            : Array.Empty<DashboardFeedItemDto>();

        var myPending = (canContracts || canApprovals)
            ? await BuildMyPendingFeedAsync(userId, canContracts, canApprovals, ct)
            : Array.Empty<DashboardFeedItemDto>();

        return Ok(new DashboardFeedDto(contracts, forms, myPending));
    }

    [HttpGet("quick-search")]
    public async Task<IActionResult> QuickSearch([FromQuery] string? q, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var term = (q ?? "").Trim();
        if (term.Length < 2)
            return Ok(new DashboardQuickSearchResultDto(Array.Empty<DashboardQuickSearchItemDto>()));

        var digits = NormalizeDigits(term);
        var canContracts = User.HasClaim("permission", "contracts.read")
            || User.HasClaim("permission", "contracts.read.all");
        var canForms = User.HasClaim("permission", "forms.read")
            || User.HasClaim("permission", "forms.read.all")
            || User.HasClaim("permission", "approvals.read");

        var items = new List<DashboardQuickSearchItemDto>();

        if (canContracts)
            items.AddRange(await SearchContractsAsync(userId, term, digits, ct));

        if (canForms)
            items.AddRange(await SearchFormSubmissionsAsync(term, digits, ct));

        return Ok(new DashboardQuickSearchResultDto(
            items
                .OrderByDescending(x => x.AtUtc)
                .Take(12)
                .ToList()));
    }

    private async Task<List<DashboardQuickSearchItemDto>> SearchContractsAsync(
        Guid userId,
        string term,
        string digits,
        CancellationToken ct)
    {
        _ = userId;
        var query = db.Contracts.AsNoTracking()
            .Where(c => !c.IsArchived)
            .ApplyVisibleContracts(User);

        query = query.Where(c =>
            c.ContractNumber.Contains(term)
            || c.Title.Contains(term)
            || c.FirstName.Contains(term)
            || c.LastName.Contains(term)
            || c.SubjectPersonName.Contains(term)
            || (digits.Length >= 3 && c.NationalId.Contains(digits))
            || (digits.Length >= 4 && c.Phone.Contains(digits))
            || (digits.Length >= 3 && c.ContractNumber.Contains(digits)));

        var rows = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(8)
            .Select(c => new
            {
                c.Id,
                c.ContractNumber,
                c.Title,
                c.FirstName,
                c.LastName,
                c.NationalId,
                c.Phone,
                c.SubjectPersonName,
                c.Status,
                c.WorkflowName,
                c.CreatedAtUtc,
                c.StepsJson,
                c.FilePath,
                c.FileName,
                c.OriginalFilePath,
            })
            .ToListAsync(ct);

        var stepsById = rows.ToDictionary(
            r => r.Id,
            r => ContractWorkflowProcessor.DeserializeSteps(r.StepsJson));
        await EnrichApproverNamesAsync(stepsById, ct);

        return rows.Select(r =>
        {
            var party = ResolvePartyName(r.FirstName, r.LastName, r.SubjectPersonName);
            var subject = ResolveContractSubject(r.Title, r.SubjectPersonName);
            var hasSigned = HasSignedContractDocument(r.StepsJson);
            var hasOriginal = !string.IsNullOrWhiteSpace(r.OriginalFilePath);
            return new DashboardQuickSearchItemDto(
                r.Id.ToString(),
                "contract",
                r.ContractNumber,
                subject,
                ToContractClientStatus(r.Status),
                party,
                r.NationalId,
                r.Phone,
                r.ContractNumber,
                r.WorkflowName,
                r.CreatedAtUtc,
                "/admin/contracts",
                !string.IsNullOrWhiteSpace(r.FilePath) || hasOriginal,
                hasSigned,
                hasOriginal,
                r.FileName,
                stepsById[r.Id]);
        }).ToList();
    }

    private async Task<List<DashboardQuickSearchItemDto>> SearchFormSubmissionsAsync(
        string term,
        string digits,
        CancellationToken ct)
    {
        var query = db.FormSubmissions.AsNoTracking()
            .Include(s => s.Form)
            .ApplyVisibleFormSubmissions(db, User);
        var words = term
            .Split([' ', '‌', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (words.Count == 0 && term.Length >= 2)
            words = [term];

        foreach (var word in words)
        {
            var w = word;
            query = query.Where(s =>
                (s.SubmitterName != null && s.SubmitterName.Contains(w))
                || (s.SubmitterEmail != null && s.SubmitterEmail.Contains(w))
                || (s.TrackingCode != null && s.TrackingCode.Contains(w))
                || (s.FieldsJson != null && s.FieldsJson.Contains(w)));
        }

        if (digits.Length >= 4)
        {
            query = query.Where(s =>
                (s.SubmitterEmail != null && s.SubmitterEmail.Contains(digits))
                || (s.TrackingCode != null && s.TrackingCode.Contains(digits))
                || (s.FieldsJson != null && s.FieldsJson.Contains(digits)));
        }
        else if (digits.Length >= 3)
        {
            query = query.Where(s => s.FieldsJson != null && s.FieldsJson.Contains(digits));
        }

        var candidates = await (
                from s in query
                join f in db.Forms.AsNoTracking() on s.FormId equals f.Id
                where !f.IsDeleted
                orderby s.SubmittedAtUtc descending
                select new
                {
                    s.Id,
                    s.SubmittedAtUtc,
                    s.SubmitterName,
                    s.SubmitterEmail,
                    s.Status,
                    s.WorkflowName,
                    s.StepsJson,
                    s.FieldsJson,
                    FormTitle = f.Title,
                })
            .Take(60)
            .ToListAsync(ct);

        var rows = candidates
            .Where(r => FormSubmissionMatchesSearch(term, digits, r.SubmitterName, r.SubmitterEmail, r.FieldsJson))
            .Take(8)
            .ToList();

        var stepsById = rows.ToDictionary(
            r => r.Id,
            r => ContractWorkflowProcessor.DeserializeSteps(r.StepsJson));
        await EnrichApproverNamesAsync(stepsById, ct);

        return rows.Select(r =>
        {
            var person = ResolveFormSubmitterPerson(r.SubmitterName, r.SubmitterEmail, r.FieldsJson);
            var subtitle = string.IsNullOrWhiteSpace(person.FullName)
                ? $"پاسخ فرم «{r.FormTitle}»"
                : $"پاسخ «{r.FormTitle}» — {person.FullName}";

            return new DashboardQuickSearchItemDto(
                r.Id.ToString(),
                "form",
                r.FormTitle,
                subtitle,
                ToFormClientStatus(r.Status),
                person.FullName,
                person.NationalId,
                person.Phone,
                null,
                r.WorkflowName,
                r.SubmittedAtUtc,
                "/admin/approvals",
                true,
                false,
                false,
                null,
                stepsById[r.Id]);
        }).ToList();
    }

    private static bool HasSignedContractDocument(string? stepsJson) =>
        ContractWorkflowProcessor.DeserializeSteps(stepsJson)
            .Any(s => string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase));

    private async Task EnrichApproverNamesAsync(
        Dictionary<Guid, List<PorslineClone.Application.Contracts.ApprovalStepDto>> stepsByEntity,
        CancellationToken ct)
    {
        var approverIds = stepsByEntity.Values
            .SelectMany(s => s)
            .Select(s => s.UserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (approverIds.Count == 0) return;

        var lookup = await userManager.Users
            .Where(u => approverIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName, u.Email })
            .ToDictionaryAsync(
                x => x.Id,
                x =>
                {
                    var full = $"{x.FirstName} {x.LastName}".Trim();
                    return new { DisplayName = string.IsNullOrWhiteSpace(full) ? x.UserName : full, x.Email };
                },
                ct);

        foreach (var steps in stepsByEntity.Values)
        {
            foreach (var step in steps)
            {
                if (!lookup.TryGetValue(step.UserId, out var p)) continue;
                if (string.IsNullOrWhiteSpace(step.UserName)) step.UserName = p.DisplayName ?? "";
                if (string.IsNullOrWhiteSpace(step.UserEmail)) step.UserEmail = p.Email;
            }
        }
    }

    private static string ResolvePartyName(string firstName, string lastName, string? subjectPerson)
    {
        var full = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        var s = subjectPerson?.Trim();
        return string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    private static string NormalizeDigits(string value)
        => value
            .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
            .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Trim();

    private static string ToContractClientStatus(ContractStatus status) => status switch
    {
        ContractStatus.Pending => "pending",
        ContractStatus.InProgress => "in_progress",
        ContractStatus.Approved => "approved",
        ContractStatus.Rejected => "rejected",
        _ => "pending"
    };

    private static string ToFormClientStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "pending",
        FormSubmissionStatus.InProgress => "in_progress",
        FormSubmissionStatus.Approved => "approved",
        FormSubmissionStatus.Rejected => "rejected",
        FormSubmissionStatus.Submitted => "submitted",
        _ => "pending"
    };

    private async Task<IReadOnlyList<DashboardFeedItemDto>> BuildContractFeedAsync(Guid userId, CancellationToken ct)
    {
        _ = userId;
        var rows = await db.Contracts.AsNoTracking()
            .Where(c => !c.IsArchived)
            .ApplyVisibleContracts(User)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(12)
            .Select(c => new
            {
                c.Id,
                c.ContractNumber,
                c.Title,
                c.SubjectPersonName,
                c.Status,
                c.CreatedAtUtc,
                c.StepsJson,
            })
            .ToListAsync(ct);

        var items = new List<DashboardFeedItemDto>();
        foreach (var c in rows)
        {
            var subject = ResolveContractSubject(c.Title, c.SubjectPersonName);
            var at = c.CreatedAtUtc;
            string message;

            if (c.Status == ContractStatus.Approved)
            {
                var lastApproved = TryGetLastStepActionUtc(c.StepsJson, "approved");
                if (lastApproved.HasValue) at = lastApproved.Value;
                message = $"قرارداد «{c.ContractNumber}» با موضوع «{subject}» تأیید نهایی شد.";
            }
            else if (c.Status == ContractStatus.Rejected)
            {
                var lastRejected = TryGetLastStepActionUtc(c.StepsJson, "rejected");
                if (lastRejected.HasValue) at = lastRejected.Value;
                message = $"قرارداد «{c.ContractNumber}» با موضوع «{subject}» رد شد.";
            }
            else if (c.Status == ContractStatus.InProgress)
            {
                message = $"قرارداد «{c.ContractNumber}» با موضوع «{subject}» در جریان تأیید است.";
            }
            else
            {
                message = $"قرارداد «{c.ContractNumber}» با موضوع «{subject}» ثبت شد.";
            }

            items.Add(new DashboardFeedItemDto(
                c.Id.ToString(),
                "contracts",
                c.ContractNumber,
                message,
                at,
                "/admin/contracts"));
        }

        return items
            .OrderByDescending(x => x.AtUtc)
            .Take(3)
            .ToList();
    }

    private async Task<IReadOnlyList<DashboardFeedItemDto>> BuildFormSubmissionFeedAsync(CancellationToken ct)
    {
        var rows = await db.FormSubmissions.AsNoTracking()
            .Include(s => s.Form)
            .ApplyVisibleFormSubmissions(db, User)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Take(3)
            .Select(s => new
            {
                s.Id,
                s.SubmittedAtUtc,
                s.SubmitterName,
                s.FieldsJson,
                FormTitle = s.Form!.Title,
            })
            .ToListAsync(ct);

        return rows.Select(s =>
        {
            var nationalId = TryExtractNationalId(s.FieldsJson);
            var who = !string.IsNullOrWhiteSpace(nationalId)
                ? $"کاربر با کد ملی {nationalId}"
                : !string.IsNullOrWhiteSpace(s.SubmitterName)
                    ? s.SubmitterName.Trim()
                    : "کاربر";

            return new DashboardFeedItemDto(
                s.Id.ToString(),
                "forms",
                s.FormTitle,
                $"{who} فرم «{s.FormTitle}» را ارسال کرد.",
                s.SubmittedAtUtc,
                "/admin/approvals");
        }).ToList();
    }

    private async Task<IReadOnlyList<DashboardFeedItemDto>> BuildMyPendingFeedAsync(
        Guid userId,
        bool canContracts,
        bool canApprovals,
        CancellationToken ct)
    {
        var items = new List<DashboardFeedItemDto>();

        if (canContracts)
        {
            var contracts = await db.Contracts.AsNoTracking()
                .Where(c => !c.IsArchived && c.Status == ContractStatus.InProgress)
                .ApplyVisibleContracts(User)
                .OrderByDescending(c => c.CreatedAtUtc)
                .Take(20)
                .Select(c => new { c.Id, c.ContractNumber, c.Title, c.SubjectPersonName, c.StepsJson, c.CreatedAtUtc })
                .ToListAsync(ct);

            foreach (var c in contracts)
            {
                if (!IsPendingForUser(c.StepsJson, userId)) continue;
                var subject = ResolveContractSubject(c.Title, c.SubjectPersonName);
                items.Add(new DashboardFeedItemDto(
                    c.Id.ToString(),
                    "pending",
                    c.ContractNumber,
                    $"نوبت شما: تأیید قرارداد «{c.ContractNumber}» — {subject}",
                    c.CreatedAtUtc,
                    "/admin/contracts"));
            }
        }

        if (canApprovals)
        {
            var submissions = await (
                from s in db.FormSubmissions.AsNoTracking()
                    .Include(x => x.Form)
                    .ApplyVisibleFormSubmissions(db, User)
                join f in db.Forms.AsNoTracking() on s.FormId equals f.Id
                where !f.IsDeleted && s.Status == FormSubmissionStatus.InProgress
                orderby s.SubmittedAtUtc descending
                select new { s.Id, s.StepsJson, s.SubmittedAtUtc, FormTitle = f.Title })
                .Take(20)
                .ToListAsync(ct);

            foreach (var s in submissions)
            {
                if (!IsPendingForUser(s.StepsJson, userId)) continue;
                items.Add(new DashboardFeedItemDto(
                    s.Id.ToString(),
                    "pending",
                    s.FormTitle,
                    $"نوبت شما: تأیید پاسخ فرم «{s.FormTitle}»",
                    s.SubmittedAtUtc,
                    "/admin/approvals"));
            }
        }

        return items
            .OrderByDescending(x => x.AtUtc)
            .Take(3)
            .ToList();
    }

    private static string ResolveContractSubject(string title, string? subjectPerson)
    {
        var t = title?.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t;
        var p = subjectPerson?.Trim();
        if (!string.IsNullOrWhiteSpace(p)) return p;
        return "بدون موضوع";
    }

    private static DateTime? TryGetLastStepActionUtc(string? stepsJson, string status)
    {
        var steps = ContractWorkflowProcessor.DeserializeSteps(stepsJson);
        return steps
            .Where(s => string.Equals(s.Status, status, StringComparison.OrdinalIgnoreCase) && s.ActionAt.HasValue)
            .OrderByDescending(s => s.ActionAt)
            .Select(s => s.ActionAt)
            .FirstOrDefault();
    }

    private static bool IsPendingForUser(string? stepsJson, Guid userId)
    {
        var steps = ContractWorkflowProcessor.DeserializeSteps(stepsJson);
        return steps.Any(s =>
            string.Equals(s.Status, "pending", StringComparison.OrdinalIgnoreCase)
            && s.UserId == userId);
    }

    private sealed record FormSubmitterPerson(string FullName, string? Phone, string? NationalId);

    private static List<FormFieldValueDto>? ParseFormFields(string? fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<FormFieldValueDto>>(fieldsJson, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFieldLabel(string? label)
        => (label ?? "").Replace(" ", "").Replace("‌", "").Trim();

    private static bool FieldLabelMatches(string? label, params string[] keywords)
    {
        var n = NormalizeFieldLabel(label);
        return keywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static FormSubmitterPerson ResolveFormSubmitterPerson(
        string? submitterName,
        string? submitterEmail,
        string? fieldsJson)
    {
        var fields = ParseFormFields(fieldsJson);
        string? first = null;
        string? last = null;
        string? fullFromField = null;
        string? phone = null;
        string? nationalId = null;

        if (fields is not null)
        {
            foreach (var f in fields)
            {
                var val = (f.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(val)) continue;

                if (FieldLabelMatches(f.Label, "نام", "name")
                    && !FieldLabelMatches(f.Label, "خانواد", "family", "فامیل", "نامخانواد", "lastname"))
                {
                    first ??= val;
                    continue;
                }

                if (FieldLabelMatches(f.Label, "نامخانواد", "نام_خانواد", "family", "فامیل", "lastname", "نام خانوادگی"))
                {
                    last ??= val;
                    continue;
                }

                if (FieldLabelMatches(f.Label, "نامکامل", "نام_کامل")
                    || (FieldLabelMatches(f.Label, "نام") && NormalizeFieldLabel(f.Label).Contains("خانواد", StringComparison.Ordinal)))
                {
                    fullFromField ??= val;
                    continue;
                }

                if (FieldLabelMatches(f.Label, "موبایل", "تلفن", "شمارهتماس", "شماره_تماس", "mobile", "phone"))
                {
                    var p = NormalizeDigits(val);
                    if (p.Length >= 10) phone ??= p;
                    continue;
                }

                if (FieldLabelMatches(f.Label, "کدملی", "کد_ملی", "national"))
                {
                    var id = NormalizeDigits(val);
                    if (id.Length == 10) nationalId ??= id;
                }
            }

            nationalId ??= TryExtractNationalId(fieldsJson);

            if (string.IsNullOrWhiteSpace(phone))
            {
                foreach (var f in fields)
                {
                    var p = NormalizeDigits(f.Value ?? "");
                    if (p.Length is >= 10 and <= 11 && p.StartsWith("09", StringComparison.Ordinal))
                    {
                        phone = p;
                        break;
                    }
                }
            }
        }

        var built = $"{first ?? ""} {last ?? ""}".Trim();
        if (string.IsNullOrWhiteSpace(built)) built = fullFromField?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(built)) built = submitterName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(phone))
        {
            var emailDigits = NormalizeDigits(submitterEmail ?? "");
            if (emailDigits.Length is >= 10 and <= 11 && emailDigits.StartsWith("09", StringComparison.Ordinal))
                phone = emailDigits;
        }

        if (string.IsNullOrWhiteSpace(built) && nationalId is not null)
            built = $"کد ملی {nationalId}";

        return new FormSubmitterPerson(
            string.IsNullOrWhiteSpace(built) ? "—" : built,
            phone,
            nationalId);
    }

    private static bool FormSubmissionMatchesSearch(
        string term,
        string digits,
        string? submitterName,
        string? submitterEmail,
        string? fieldsJson)
    {
        var person = ResolveFormSubmitterPerson(submitterName, submitterEmail, fieldsJson);
        var fields = ParseFormFields(fieldsJson);

        var haystacks = new List<string>();
        if (!string.IsNullOrWhiteSpace(submitterName)) haystacks.Add(submitterName);
        if (!string.IsNullOrWhiteSpace(submitterEmail)) haystacks.Add(submitterEmail);
        if (!string.IsNullOrWhiteSpace(person.FullName)) haystacks.Add(person.FullName);
        if (!string.IsNullOrWhiteSpace(person.Phone)) haystacks.Add(person.Phone);
        if (!string.IsNullOrWhiteSpace(person.NationalId)) haystacks.Add(person.NationalId);

        if (fields is not null)
        {
            foreach (var f in fields)
            {
                if (!string.IsNullOrWhiteSpace(f.Value)) haystacks.Add(f.Value);
                if (!string.IsNullOrWhiteSpace(f.Label)) haystacks.Add(f.Label);
            }
        }

        var combined = string.Join(" ", haystacks);

        if (digits.Length >= 4)
        {
            var phoneHay = string.Concat(haystacks.Select(NormalizeDigits));
            if (phoneHay.Contains(digits, StringComparison.Ordinal)) return true;
        }

        var words = term
            .Split([' ', '‌', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2)
            .ToList();

        if (words.Count >= 2)
        {
            return words.All(w =>
                combined.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        if (term.Length >= 2 && combined.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;

        return digits.Length >= 3 && combined.Contains(digits, StringComparison.Ordinal);
    }

    private static string? TryExtractNationalId(string? fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson)) return null;

        var fields = ParseFormFields(fieldsJson);
        if (fields is null) return null;

        foreach (var f in fields)
        {
            if (FieldLabelMatches(f.Label, "کدملی", "کد_ملی"))
            {
                var idDigits = NormalizeDigits(f.Value ?? "");
                if (idDigits.Length == 10) return idDigits;
            }
        }

        foreach (var f in fields)
        {
            var idDigits = NormalizeDigits(f.Value ?? "");
            if (idDigits.Length == 10) return idDigits;
        }

        return null;
    }
}
