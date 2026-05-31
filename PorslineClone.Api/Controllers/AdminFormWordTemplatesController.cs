using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Application.FormWordTemplates;
using PorslineClone.Infrastructure.Services.FormWordTemplates;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/form-word-templates")]
[Authorize]
public class AdminFormWordTemplatesController(FormWordTemplateService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    [HttpGet("by-form/{formId:guid}")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> GetByForm(Guid formId, CancellationToken ct)
    {
        var d = await service.GetByFormIdAsync(formId, ct);
        return d is null ? NotFound(new { message = "قالب تبدیل برای این فرم یافت نشد" }) : Ok(d);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var d = await service.GetAsync(id, ct);
        return d is null ? NotFound(new { message = "قالب یافت نشد" }) : Ok(d);
    }

    [HttpPost]
    [Authorize(Policy = "forms.add")]
    public async Task<IActionResult> Create([FromBody] CreateFormWordTemplateRequest req, CancellationToken ct)
    {
        try
        {
            var d = await service.CreateAsync(req.FormId, req.Name ?? "قالب تبدیل", ct);
            return Ok(d);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/upload-docx")]
    [Authorize(Policy = "forms.update")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocx(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل Word انتخاب نشده است" });
        try
        {
            return Ok(await service.UploadDocxAsync(id, file, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/mappings")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> SaveMappings(Guid id, [FromBody] SaveFormWordMappingsRequest req, CancellationToken ct)
    {
        try
        {
            return Ok(await service.SaveMappingsAsync(
                id, req.Mappings ?? [], req.SignaturePlaceholderKey, req.StampPlaceholderKey, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/upload-signature")]
    [Authorize(Policy = "forms.update")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadSignature(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل امضا انتخاب نشده است" });
        try
        {
            return Ok(await service.UploadSignatureAsync(id, file, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/upload-stamp")]
    [Authorize(Policy = "forms.update")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadStamp(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل مهر انتخاب نشده است" });
        try
        {
            return Ok(await service.UploadStampAsync(id, file, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await service.SoftDeleteAsync(id, ct);
        if (!ok) return NotFound(new { message = "قالب یافت نشد" });
        return Ok(new { message = "قالب حذف نرم شد؛ فرم برای اتصال قالب جدید آزاد است" });
    }
}

public class CreateFormWordTemplateRequest
{
    public Guid FormId { get; set; }
    public string? Name { get; set; }
}

public class SaveFormWordMappingsRequest
{
    public List<FormWordFieldMappingDto>? Mappings { get; set; }
    public string? SignaturePlaceholderKey { get; set; }
    public string? StampPlaceholderKey { get; set; }
}
