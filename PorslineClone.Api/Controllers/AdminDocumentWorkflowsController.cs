using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-workflows")]
[Authorize]
public class AdminDocumentWorkflowsController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.DocumentWorkflowTemplates
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var creatorIds = rows
            .Where(x => x.CreatedByUserId.HasValue)
            .Select(x => x.CreatedByUserId!.Value)
            .Distinct()
            .ToList();
        var creatorLookup = creatorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => creatorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
                .ToDictionaryAsync(
                    u => u.Id,
                    u =>
                    {
                        var full = $"{u.FirstName} {u.LastName}".Trim();
                        return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;
                    },
                    ct);

        var items = rows.Select(x =>
        {
            string? createdByName = null;
            if (x.CreatedByUserId.HasValue && creatorLookup.TryGetValue(x.CreatedByUserId.Value, out var n))
                createdByName = string.IsNullOrWhiteSpace(n) ? null : n;
            return new DocumentWorkflowTemplateListItemDto(
                x.Id,
                x.Name,
                DeserializeSteps(x.StepsJson).Count,
                x.IsActive,
                x.CreatedAtUtc,
                x.CreatedByUserId,
                createdByName);
        }).ToList();
        return Ok(items);
    }

    [HttpGet("active")]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var rows = await db.DocumentWorkflowTemplates
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var items = rows.Select(x => new
        {
            x.Id,
            x.Name,
            approverCount = DeserializeSteps(x.StepsJson).Count,
        }).ToList();
        return Ok(items);
    }

    [HttpGet("action-directions")]
    [Authorize(Policy = "documents.workflow.read")]
    public IActionResult ActionDirections() =>
        Ok(PostApprovalDirections.Items.Select(x => new { key = x.Key, label = x.Label }));

    [HttpGet("workflow-users")]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> WorkflowUsers(CancellationToken ct)
    {
        var users = await db.Users
            .Where(x => !x.IsSoftDeleted && x.IsActive)
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                Name = (x.FirstName + " " + x.LastName).Trim(),
                Email = x.Email ?? (x.PhoneNumber ?? ""),
                x.AvatarUrl,
                PositionName = x.UserPosition != null ? x.UserPosition.Name : null,
                HasSignature = x.SignatureImagePath != null && x.SignatureImagePath != "",
            })
            .ToListAsync(ct);

        return Ok(users.Select(u => new
        {
            u.Id,
            u.FirstName,
            u.LastName,
            u.Name,
            u.Email,
            AvatarUrl = ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, u.Id, u.AvatarUrl),
            u.PositionName,
            u.HasSignature,
        }));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var x = await db.DocumentWorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (x is null || !x.IsActive) return NotFound(new { message = "گردش یافت نشد یا حذف شده است" });

        var steps = DeserializeSteps(x.StepsJson);
        return Ok(MapDetail(x, steps));
    }

    [HttpPost]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Create([FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.DocumentWorkflowTemplates.AnyAsync(x => x.Name == name && x.IsActive, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            userId = uid;

        var (dirKey, dirLabel, assignees, actionErr) = ResolveActionConfig(req);
        if (actionErr is not null) return BadRequest(new { message = actionErr });

        var signatureErr = await ValidateWorkflowUserSignaturesAsync(cleaned, assignees, ct);
        if (signatureErr is not null) return BadRequest(new { message = signatureErr });

        var entity = new DocumentWorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            StepsJson = JsonSerializer.Serialize(cleaned),
            ActionDirectionKey = dirKey,
            ActionDirectionLabel = dirLabel,
            ActionAssigneeUserIdsJson = PostApprovalJsonHelper.SerializeUserIds(assignees),
            CanvasLayoutJson = SerializeCanvasLayout(req.CanvasLayout),
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DocumentWorkflowTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(MapDetail(entity, cleaned));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var entity = await db.DocumentWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });
        if (!entity.IsActive) return BadRequest(new { message = "این گردش حذف شده و قابل ویرایش نیست" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.DocumentWorkflowTemplates.AnyAsync(x => x.Name == name && x.Id != id && x.IsActive, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        var (dirKey, dirLabel, assignees, actionErr) = ResolveActionConfig(req);
        if (actionErr is not null) return BadRequest(new { message = actionErr });

        var signatureErr = await ValidateWorkflowUserSignaturesAsync(cleaned, assignees, ct);
        if (signatureErr is not null) return BadRequest(new { message = signatureErr });

        entity.Name = name;
        entity.StepsJson = JsonSerializer.Serialize(cleaned);
        entity.ActionDirectionKey = dirKey;
        entity.ActionDirectionLabel = dirLabel;
        entity.ActionAssigneeUserIdsJson = PostApprovalJsonHelper.SerializeUserIds(assignees);
        entity.CanvasLayoutJson = SerializeCanvasLayout(req.CanvasLayout);
        await db.SaveChangesAsync(ct);
        return Ok(MapDetail(entity, cleaned));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "documents.workflow.delete")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var entity = await db.DocumentWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });

        var documentsUsage = await WorkflowTemplateSoftDelete.CountDocumentUsageAsync(db, id, ct);
        if (entity.IsActive)
        {
            entity.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        var usage = new WorkflowTemplateSoftDelete.UsageCounts(0, 0, 0, documentsUsage);
        var message = WorkflowTemplateSoftDelete.BuildDeleteMessage(entity.Name, usage, isFormWorkflow: false, isDocumentWorkflow: true);
        return Ok(new { message, documentsUsage, isActive = false });
    }

    private static List<WorkflowStepDto> CleanSteps(List<WorkflowStepDto> steps) =>
        steps
            .Where(x => x.UserId != Guid.Empty)
            .OrderBy(x => x.Order)
            .Select((x, i) => x with
            {
                Order = i + 1,
                OnReject = x.OnReject is "continue" ? "continue" : "stop"
            })
            .ToList();

    private static List<WorkflowStepDto> DeserializeSteps(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<WorkflowStepDto>>(json) ?? []);

    private static DocumentWorkflowTemplateDetailDto MapDetail(
        DocumentWorkflowTemplate entity,
        List<WorkflowStepDto> steps) =>
        new(
            entity.Id,
            entity.Name,
            entity.IsActive,
            steps,
            entity.ActionDirectionKey,
            entity.ActionDirectionLabel,
            PostApprovalJsonHelper.ParseUserIds(entity.ActionAssigneeUserIdsJson),
            DeserializeCanvasLayout(entity.CanvasLayoutJson));

    private static string? SerializeCanvasLayout(WorkflowCanvasLayoutDto? layout) =>
        layout is null ? null : JsonSerializer.Serialize(layout);

    private static WorkflowCanvasLayoutDto? DeserializeCanvasLayout(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<WorkflowCanvasLayoutDto>(json);

    private static (string? Key, string? Label, List<Guid> Assignees, string? Error) ResolveActionConfig(
        SaveWorkflowTemplateRequest req)
    {
        var assignees = (req.ActionAssigneeUserIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        var key = (req.ActionDirectionKey ?? "").Trim();
        if (assignees.Count == 0 && string.IsNullOrWhiteSpace(key))
            return (null, null, [], null);

        if (assignees.Count == 0)
            return (null, null, [], "حداقل یک اقدام‌کننده انتخاب کنید");

        if (string.IsNullOrWhiteSpace(key))
            return (null, null, [], "جهت اقدام را انتخاب کنید");

        var label = PostApprovalDirections.LabelFor(key);
        if (label is null)
            return (null, null, [], "جهت اقدام نامعتبر است");

        return (key, label, assignees, null);
    }

    private Task<string?> ValidateWorkflowUserSignaturesAsync(
        List<WorkflowStepDto> steps,
        List<Guid> assignees,
        CancellationToken ct) =>
        WorkflowUserSignatureValidator.ValidateUserIdsAsync(
            db,
            steps.Select(s => s.UserId).Concat(assignees),
            ct);
}
