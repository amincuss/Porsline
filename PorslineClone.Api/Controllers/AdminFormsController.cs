using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Api.RuleEngine;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/forms")]
[Authorize]
public class AdminFormsController(AppDbContext db, IRuleEvaluationService ruleEvaluationService, IWebHostEnvironment env, ISmsSender smsSender, IFrontendUrlResolver frontendUrls) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.IsInRole("Admin");
    private bool CanReadAllForms => User.HasClaim("permission", "forms.read.all");

    private IQueryable<Form> ScopeVisibleForms(IQueryable<Form> query)
    {
        if (IsAdmin || CanReadAllForms) return query;
        var userId = CurrentUserId;
        if (!Guid.TryParse(userId, out var userGuid))
            return query.Where(_ => false);

        return query.Where(f =>
            f.UserId == userId ||
            db.FormUserAccesses.Any(a => a.FormId == f.Id && a.UserId == userGuid));
    }

    [HttpGet]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userIds = await db.Forms
            .Where(f => !f.IsDeleted)
            .Select(f => f.UserId)
            .Where(x => x != null && x != "")
            .Distinct()
            .ToListAsync(ct);
        var creatorIds = userIds
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        var creators = await db.Users
            .Where(x => creatorIds.Contains(x.Id))
            .Select(x => new { UserId = x.Id.ToString(), FullName = (x.LastName + " " + x.FirstName).Trim() })
            .ToListAsync(ct);
        var creatorMap = creators
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key!, g => g.First().FullName);

        var forms = await ScopeVisibleForms(db.Forms)
            .Where(f => !f.IsDeleted)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new
            {
                f.Id,
                f.Title,
                f.Description,
                f.ExpiresAtUtc,
                f.CreatedAtUtc,
                f.IsActive,
                FieldCount = db.FormFields.Count(ff => ff.FormId == f.Id),
                f.UserId
            })
            .ToListAsync(ct);

        return Ok(forms.Select(f => new
        {
            f.Id,
            f.Title,
            f.Description,
            f.ExpiresAtUtc,
            f.CreatedAtUtc,
            f.IsActive,
            f.FieldCount,
            CreatorName = !string.IsNullOrWhiteSpace(f.UserId) && creatorMap.TryGetValue(f.UserId, out var full) ? full : "-"
        }));
    }

    [HttpPost]
    [Authorize(Policy = "forms.add")]
    public async Task<IActionResult> Create([FromBody] CreateFormRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var form = new Form
        {
            Id = Guid.NewGuid(),
            Title = req.Title?.Trim() is { Length: > 0 } t ? t : "فرم بدون عنوان",
            Description = req.Description?.Trim(),
            ExpiresAtUtc = req.ExpiresAtUtc,
            QuestionDisplayMode = req.QuestionDisplayMode is "single" ? "single" : "all",
            IsActive = true,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync(ct);
        return Ok(new { form.Id, form.Title, form.Description, form.CreatedAtUtc, FieldCount = 0 });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms)
            .Include(f => f.Fields.OrderBy(ff => ff.SortOrder))
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);

        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        return Ok(new
        {
            form.Id,
            form.Title,
            form.Description,
            form.ExpiresAtUtc,
            form.QuestionDisplayMode,
            form.ApprovalEnabled,
            form.IsActive,
            Fields = form.Fields.Select(f => new
            {
                f.Id,
                f.FieldType,
                f.Label,
                f.Placeholder,
                f.HelpText,
                f.IsRequired,
                f.SortOrder,
                f.ColSpan,
                Options = f.OptionsJson != null
                    ? JsonSerializer.Deserialize<List<string>>(f.OptionsJson)
                    : null,
                Conditions = f.ConditionsJson != null
                    ? JsonSerializer.Deserialize<List<ConditionRuleDto>>(f.ConditionsJson)
                    : null,
                Rules = f.ConditionsJson != null
                    ? JsonSerializer.Deserialize<List<RuleDefinition>>(f.ConditionsJson)
                    : null,
                f.UploadMaxSizeMb,
                f.RowId,
                f.ColIndex,
                f.RowColCount
            })
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateFormRequest req, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        form.Title = req.Title?.Trim() is { Length: > 0 } t ? t : form.Title;
        form.Description = req.Description?.Trim();
        form.ExpiresAtUtc = req.ExpiresAtUtc;
        form.QuestionDisplayMode = req.QuestionDisplayMode is "single" ? "single" : "all";
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "فرم به‌روز شد" });
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateFormStatusRequest req, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        form.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "وضعیت فرم بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است و امکان ثبت پاسخ ندارد" });

        form.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "فرم حذف شد" });
    }

    [HttpPut("{id:guid}/fields")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> SaveFields(Guid id, [FromBody] SaveFieldsRequest req, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        var incoming = req?.Fields ?? [];
        var existing = await db.FormFields.Where(f => f.FormId == id).ToListAsync(ct);
        db.FormFields.RemoveRange(existing);

        for (int i = 0; i < incoming.Count; i++)
        {
            var f = incoming[i];
            var mappedConditions = MapIncomingConditions(f.Conditions);
            var mappedRules = MapIncomingRules(f.Rules);

            db.FormFields.Add(new FormField
            {
                Id = ParseOrCreateFieldId(f.Id),
                FormId = id,
                FieldType = (FieldType)f.FieldType,
                Label = f.Label?.Trim() ?? "",
                Placeholder = f.Placeholder?.Trim(),
                HelpText = f.HelpText?.Trim(),
                IsRequired = f.IsRequired,
                SortOrder = i,
                ColSpan = f.ColSpan is 4 or 6 or 12 ? f.ColSpan : 12,
                OptionsJson = f.Options is { Count: > 0 }
                    ? JsonSerializer.Serialize(f.Options)
                    : null,
                ConditionsJson = mappedConditions is { Count: > 0 }
                    ? JsonSerializer.Serialize(mappedConditions)
                    : mappedRules is { Count: > 0 }
                        ? JsonSerializer.Serialize(mappedRules)
                    : null,
                RowId = string.IsNullOrWhiteSpace(f.RowId) ? null : f.RowId.Trim(),
                ColIndex = f.ColIndex,
                RowColCount = f.RowColCount is 1 or 2 or 3 ? f.RowColCount : 1,
                UploadMaxSizeMb = f.UploadMaxSizeMb is > 0 and <= 100 ? f.UploadMaxSizeMb : null,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "فرم ذخیره شد" });
    }

    /// <summary>Accepts any string from the client; invalid or missing GUIDs become a new id.</summary>
    private static Guid ParseOrCreateFieldId(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s.Trim(), out var g) && g != Guid.Empty)
            return g;
        return Guid.NewGuid();
    }

    private static Guid ParseGuidRef(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s.Trim(), out var g))
            return g;
        return Guid.Empty;
    }

    private static Guid? ParseNullableGuid(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s.Trim(), out var g))
            return g;
        return null;
    }

    private static List<ConditionRuleDto>? MapIncomingConditions(List<SaveConditionPayload>? list)
    {
        if (list is not { Count: > 0 }) return null;
        return list.ConvertAll(c => new ConditionRuleDto(
            ParseOrCreateFieldId(c.Id),
            ParseGuidRef(c.SourceFieldId),
            string.IsNullOrWhiteSpace(c.Operator) ? "equals" : c.Operator.Trim(),
            c.Value ?? "",
            string.IsNullOrWhiteSpace(c.Action) ? "show" : c.Action.Trim()));
    }

    private static List<RuleDefinition>? MapIncomingRules(List<SaveRulePayload>? list)
    {
        if (list is not { Count: > 0 }) return null;
        return list.ConvertAll(r => new RuleDefinition
        {
            Conditions = r.Conditions?.ConvertAll(c => new RuleCondition
            {
                Expression = c.Expression,
                SourceFieldId = ParseNullableGuid(c.SourceFieldId),
                Operator = string.IsNullOrWhiteSpace(c.Operator) ? "equals" : c.Operator.Trim(),
                Value = c.Value,
                Value2 = c.Value2,
                Values = c.Values,
            }) ?? [],
            ConditionOperator = string.Equals(r.ConditionOperator, "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND",
            Actions = r.Actions?.ConvertAll(a => new RuleAction
            {
                Type = string.IsNullOrWhiteSpace(a.Type) ? "Show" : a.Type.Trim(),
                TargetField = ParseNullableGuid(a.TargetField),
                ValueExpression = a.ValueExpression,
                Message = a.Message,
            }) ?? [],
        });
    }

    [HttpGet("{id:guid}/workflow")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> GetWorkflow(Guid id, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        var steps = string.IsNullOrWhiteSpace(form.ApprovalWorkflowJson)
            ? new List<WorkflowStepDto>()
            : (JsonSerializer.Deserialize<List<WorkflowStepDto>>(form.ApprovalWorkflowJson) ?? new List<WorkflowStepDto>());
        return Ok(new WorkflowSettingsDto(form.ApprovalEnabled, steps.OrderBy(x => x.Order).ToList()));
    }

    [HttpPut("{id:guid}/workflow")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> SaveWorkflow(Guid id, [FromBody] SaveWorkflowRequest req, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms).FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        var cleaned = req.Steps
            .Where(x => x.UserId != Guid.Empty)
            .OrderBy(x => x.Order)
            .Select((x, i) => x with { Order = i + 1, OnReject = x.OnReject is "continue" ? "continue" : "stop" })
            .ToList();
        form.ApprovalEnabled = req.Enabled;
        form.ApprovalWorkflowJson = req.Enabled ? JsonSerializer.Serialize(cleaned) : null;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گردش تأیید ذخیره شد" });
    }

    [HttpPut("{formId:guid}/fields/{fieldId:guid}/rules")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> SaveRule(Guid formId, Guid fieldId, [FromBody] SaveFieldRulesRequest req, CancellationToken ct)
    {
        var allowedFormIds = ScopeVisibleForms(db.Forms).Select(x => x.Id);
        var field = await db.FormFields
            .Join(db.Forms, f => f.FormId, fm => fm.Id, (f, fm) => new { f, fm })
            .Where(x => x.f.FormId == formId && x.f.Id == fieldId && !x.fm.IsDeleted && allowedFormIds.Contains(x.fm.Id))
            .Select(x => x.f)
            .FirstOrDefaultAsync(ct);
        if (field is null) return NotFound(new { message = "فیلد یافت نشد" });
        field.ConditionsJson = req.Rules is { Count: > 0 } ? JsonSerializer.Serialize(req.Rules) : null;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "قوانین ذخیره شد" });
    }

    [HttpGet("{formId:guid}/fields/{fieldId:guid}/rules")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> GetRulesByField(Guid formId, Guid fieldId, CancellationToken ct)
    {
        var allowedFormIds = ScopeVisibleForms(db.Forms).Select(x => x.Id);
        var rulesJson = await db.FormFields
            .Join(db.Forms, f => f.FormId, fm => fm.Id, (f, fm) => new { f, fm })
            .Where(x => x.f.FormId == formId && x.f.Id == fieldId && !x.fm.IsDeleted && allowedFormIds.Contains(x.fm.Id))
            .Select(x => x.f.ConditionsJson)
            .FirstOrDefaultAsync(ct);
        var rules = string.IsNullOrWhiteSpace(rulesJson)
            ? new List<RuleDefinition>()
            : JsonSerializer.Deserialize<List<RuleDefinition>>(rulesJson) ?? new List<RuleDefinition>();
        return Ok(rules);
    }

    [HttpPost("{formId:guid}/rules/evaluate")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> EvaluateRulesOnSubmit(Guid formId, [FromBody] RuleEvaluationRequest req, CancellationToken ct)
    {
        var exists = await ScopeVisibleForms(db.Forms).AnyAsync(x => x.Id == formId && !x.IsDeleted, ct);
        if (!exists) return NotFound(new { message = "فرم یافت نشد" });
        var result = ruleEvaluationService.Evaluate(req.Rules ?? new(), req.Values ?? new());
        return Ok(result);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Submit(Guid id, [FromForm] SubmitFormRequest req, CancellationToken ct)
    {
        var form = await ScopeVisibleForms(db.Forms)
            .Include(f => f.Fields.OrderBy(x => x.SortOrder))
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        var values = string.IsNullOrWhiteSpace(req.ValuesJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(req.ValuesJson) ?? new Dictionary<string, string>();

        var responderId = (req.ResponderId ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown").Trim();
        responderId = Regex.Replace(responderId, @"[^\w\-]", "_");
        if (string.IsNullOrWhiteSpace(responderId)) responderId = "unknown";

        var uploadRoot = Path.Combine(env.ContentRootPath, "Formupload", responderId);
        Directory.CreateDirectory(uploadRoot);

        var filesByFieldId = Request.Form.Files
            .Where(f => f.Name.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                f => f.Name["file_".Length..],
                f => f,
                StringComparer.OrdinalIgnoreCase);

        foreach (var ff in form.Fields.Where(x => (int)x.FieldType == 9 || (int)x.FieldType == 16))
        {
            if (!filesByFieldId.TryGetValue(ff.Id.ToString(), out var file) || file.Length <= 0) continue;
            var maxMb = ff.UploadMaxSizeMb is > 0 and <= 100 ? ff.UploadMaxSizeMb!.Value : 10;
            var maxBytes = maxMb * 1024L * 1024L;
            if (file.Length > maxBytes)
                return BadRequest(new { message = $"حجم فایل فیلد «{ff.Label}» بیشتر از {maxMb}MB است." });
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
            var normalizedExt = ext.ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".gif"
            };
            if (!allowed.Contains(normalizedExt))
                return BadRequest(new { message = "فرمت فایل مجاز نیست. فقط PDF یا تصویر قابل آپلود است." });
            var safeName = $"{ff.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var savePath = Path.Combine(uploadRoot, safeName);
            await using var stream = System.IO.File.Create(savePath);
            await file.CopyToAsync(stream, ct);
            values[ff.Id.ToString()] = $"/Formupload/{responderId}/{safeName}";
        }

        var fieldValues = form.Fields.Select(f => new FormFieldValueDto(
            f.Label,
            values.TryGetValue(f.Id.ToString(), out var v) ? v : ""
        )).ToList();

        var steps = BuildApprovalSteps(form.ApprovalWorkflowJson);
        var hasWorkflow = form.ApprovalEnabled && steps.Count > 0;
        if (hasWorkflow) steps[0].Status = "pending";

        db.FormSubmissions.Add(new FormSubmission
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            SubmitterName = req.SubmitterName,
            SubmitterEmail = req.SubmitterEmail,
            SubmittedAtUtc = DateTime.UtcNow,
            CurrentStepOrder = hasWorkflow ? 1 : 0,
            Status = hasWorkflow ? FormSubmissionStatus.InProgress : FormSubmissionStatus.Approved,
            FieldsJson = JsonSerializer.Serialize(fieldValues),
            StepsJson = hasWorkflow ? JsonSerializer.Serialize(steps) : null
        });

        await db.SaveChangesAsync(ct);

        if (hasWorkflow)
        {
            var firstStep = steps.FirstOrDefault(x => x.Status == "pending");
            if (firstStep is not null)
                await SendInitialApprovalSmsAsync(firstStep.UserId, form.Title, ct);
        }

        return Ok(new { message = "فرم ثبت شد" });
    }

    private async Task SendInitialApprovalSmsAsync(Guid approverUserId, string formTitle, CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled) return;

        var approver = await db.Users.FirstOrDefaultAsync(x => x.Id == approverUserId, ct);
        if (approver is null || string.IsNullOrWhiteSpace(approver.PhoneNumber)) return;

        var msg =
            $"یک درخواست جدید از فرم «{formTitle}» برای تایید شما ثبت شد.\n" +
            $"لطفا به پنل مدیریت بخش تاییدیه‌ها مراجعه کنید.";
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        if (!string.IsNullOrWhiteSpace(adminBase))
            msg += $"\nلینک مستقیم: {adminBase}/admin/approvals";

        await smsSender.SendSmsAsync(new PorslineClone.Application.Contracts.SmsRequest(approver.PhoneNumber, msg), ct);
    }

    private static List<ApprovalStepDto> BuildApprovalSteps(string? workflowJson)
    {
        if (string.IsNullOrWhiteSpace(workflowJson)) return new List<ApprovalStepDto>();
        var workflow = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowJson) ?? new List<WorkflowStepDto>();
        return workflow
            .OrderBy(x => x.Order)
            .Select((x, i) => new ApprovalStepDto
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                UserId = x.UserId,
                Status = i == 0 ? "pending" : "waiting",
                OnReject = x.OnReject is "continue" ? "continue" : "stop",
                Note = x.Note
            })
            .ToList();
    }
}

public record CreateFormRequest(string? Title, string? Description, string? QuestionDisplayMode = "all", DateTime? ExpiresAtUtc = null);

/// <summary>PUT body for saving form fields — uses loose string ids so JSON never fails on palette/wizard/legacy values.</summary>
public class SaveFieldsRequest
{
    public List<SaveFieldPayload>? Fields { get; set; }
}

public class SaveFieldPayload
{
    public string? Id { get; set; }
    public int FieldType { get; set; }
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public int ColSpan { get; set; }
    public List<string>? Options { get; set; }
    public List<SaveConditionPayload>? Conditions { get; set; }
    public List<SaveRulePayload>? Rules { get; set; }
    public string? RowId { get; set; }
    public int ColIndex { get; set; }
    public int RowColCount { get; set; } = 1;
    public int? UploadMaxSizeMb { get; set; }
}

public class SaveConditionPayload
{
    public string? Id { get; set; }
    public string? SourceFieldId { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Action { get; set; }
}

public class SaveRulePayload
{
    public List<SaveRuleConditionPayload>? Conditions { get; set; }
    public string? ConditionOperator { get; set; }
    public List<SaveRuleActionPayload>? Actions { get; set; }
}

public class SaveRuleConditionPayload
{
    public string? Expression { get; set; }
    public string? SourceFieldId { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Value2 { get; set; }
    public List<string>? Values { get; set; }
}

public class SaveRuleActionPayload
{
    public string? Type { get; set; }
    public string? TargetField { get; set; }
    public string? ValueExpression { get; set; }
    public string? Message { get; set; }
}

public record ConditionRuleDto(Guid Id, Guid SourceFieldId, string Operator, string Value, string Action);
public record WorkflowStepDto(string Id, int Order, Guid UserId, string? Note, string OnReject = "stop");
public record SaveWorkflowRequest(bool Enabled, List<WorkflowStepDto> Steps);
public record WorkflowSettingsDto(bool Enabled, List<WorkflowStepDto> Steps);
public record SaveFieldRulesRequest(List<RuleDefinition> Rules);
public record UpdateFormStatusRequest(bool IsActive);
public class SubmitFormRequest
{
    public string? ResponderId { get; set; }
    public string? SubmitterName { get; set; }
    public string? SubmitterEmail { get; set; }
    public string? ValuesJson { get; set; }
}
