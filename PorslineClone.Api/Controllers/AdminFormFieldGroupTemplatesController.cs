using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/form-field-groups")]
[Authorize]
public class AdminFormFieldGroupTemplatesController(AppDbContext db) : ControllerBase
{
    private static IQueryable<FormFieldGroupTemplate> LiveTemplates(IQueryable<FormFieldGroupTemplate> q) =>
        q.Where(x => !x.IsDeleted);

    private Guid? CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : null;
        }
    }

    [HttpGet]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await LiveTemplates(db.FormFieldGroupTemplates)
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                fieldCount = x.FieldCount,
                x.UpdatedAtUtc,
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await LiveTemplates(db.FormFieldGroupTemplates).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(new { message = "قالب یافت نشد" });

        var fields = DeserializeFields(row.FieldsJson);
        return Ok(new
        {
            row.Id,
            row.Name,
            row.Description,
            fields,
            row.UpdatedAtUtc,
            fieldCount = fields.Count(f => (FieldType)f.FieldType != FieldType.WizardStepHeader),
        });
    }

    [HttpPost]
    [Authorize(Policy = "forms.add")]
    public async Task<IActionResult> Create([FromBody] SaveFieldGroupTemplateRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام قالب الزامی است" });

        var fields = req.Fields ?? [];
        if (fields.Count(f => (FieldType)f.FieldType == FieldType.PersonalPhoto) > 1)
            return BadRequest(new { message = "فقط یک فیلد عکس پرسنلی در هر قالب مجاز است" });

        var fieldsJson = JsonSerializer.Serialize(fields);
        var now = DateTime.UtcNow;
        var entity = new FormFieldGroupTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            FieldsJson = fieldsJson,
            FieldCount = FormFieldGroupJsonHelper.CountNonHeaderFields(fieldsJson),
            CreatedByUserId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsDeleted = false,
        };
        db.FormFieldGroupTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new { entity.Id, message = "قالب ذخیره شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveFieldGroupTemplateRequest req, CancellationToken ct)
    {
        var entity = await LiveTemplates(db.FormFieldGroupTemplates).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "قالب یافت نشد" });

        var name = (req.Name ?? "").Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام قالب الزامی است" });

        var fields = req.Fields ?? [];
        if (fields.Count(f => (FieldType)f.FieldType == FieldType.PersonalPhoto) > 1)
            return BadRequest(new { message = "فقط یک فیلد عکس پرسنلی در هر قالب مجاز است" });

        entity.Name = name;
        entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        entity.FieldsJson = JsonSerializer.Serialize(fields);
        entity.FieldCount = FormFieldGroupJsonHelper.CountNonHeaderFields(entity.FieldsJson);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "قالب بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await LiveTemplates(db.FormFieldGroupTemplates).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "قالب یافت نشد" });
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "قالب به‌صورت حذف نرم حذف شد" });
    }

    private static List<SaveFieldGroupFieldPayload> DeserializeFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<SaveFieldGroupFieldPayload>>(json) ?? [];
    }
}

public class SaveFieldGroupTemplateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<SaveFieldGroupFieldPayload>? Fields { get; set; }
}

public class SaveFieldGroupFieldPayload
{
    public string? Id { get; set; }
    public int FieldType { get; set; }
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public int ColSpan { get; set; } = 12;
    public string? DefaultValue { get; set; }
    public bool IsReadOnly { get; set; }
    public List<string>? Options { get; set; }
    public string? RowId { get; set; }
    public int ColIndex { get; set; }
    public int RowColCount { get; set; } = 1;
    public int? UploadMaxSizeMb { get; set; }
    public object? Conditions { get; set; }
    public object? Rules { get; set; }
}
