using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Services.ContractTemplates;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/contract-document-templates")]
public class AdminContractDocumentTemplatesController(ContractDocumentTemplateService templates) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [HttpGet]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await templates.ListAsync(ct));

    [HttpGet("active")]
    [Authorize(Policy = "contracts.add")]
    public async Task<IActionResult> ListActive(CancellationToken ct)
        => Ok(await templates.ListActiveForContractCreateAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await templates.GetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> Create([FromBody] UpsertContractTemplateRequest req, CancellationToken ct)
    {
        if (!TryUserId(out var userId))
            return Unauthorized();
        try
        {
            return Ok(await templates.CreateAsync(req, userId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        try
        {
            var deleted = await templates.DeleteTemplateAsync(id, ct);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> DeleteVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        try
        {
            var deleted = await templates.DeleteVersionAsync(id, versionId, ct);
            return deleted ? Ok(await templates.GetAsync(id, ct)) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertContractTemplateRequest req, CancellationToken ct)
    {
        try
        {
            var item = await templates.UpdateAsync(id, req, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = "contracts.settings.update")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadVersion(
        Guid id,
        [FromForm] UploadContractTemplateVersionForm form,
        CancellationToken ct)
    {
        if (!TryUserId(out var userId))
            return Unauthorized();
        var file = form.File;
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل قالب الزامی است" });

        try
        {
            var item = await templates.UploadVersionAsync(id, file, form.ChangeNote, userId, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> PublishVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        try
        {
            var item = await templates.PublishVersionAsync(id, versionId, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/fields")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> SaveFields(Guid id, [FromBody] SaveContractTemplateFieldsRequest req, CancellationToken ct)
    {
        try
        {
            var item = await templates.SaveFieldsAsync(id, req, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/fields")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> SaveVersionFields(
        Guid id,
        Guid versionId,
        [FromBody] SaveContractTemplateFieldsRequest req,
        CancellationToken ct)
    {
        try
        {
            var item = await templates.SaveVersionFieldsAsync(id, versionId, req, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("pdf-conversion-status")]
    [Authorize(Policy = "contracts.settings.read")]
    public IActionResult PdfConversionStatus()
        => Ok(new { available = templates.IsPdfConversionAvailable });

    [HttpPost("{id:guid}/preview")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> Preview(Guid id, [FromBody] GenerateContractFromTemplateRequest req, CancellationToken ct)
    {
        try
        {
            var (stream, fileName, contentType, pdfFallback) = await templates.GeneratePreviewAsync(
                id, req.FieldValues, req.ExportPdf, ct);
            Response.Headers.Append("X-Pdf-Fallback-Docx", pdfFallback ? "true" : "false");
            return File(stream, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/file")]
    [Authorize(Policy = "contracts.settings.update")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceVersionFile(
        Guid id,
        Guid versionId,
        [FromForm] UploadContractTemplateVersionForm form,
        CancellationToken ct)
    {
        var file = form.File;
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل Word الزامی است" });

        try
        {
            var item = await templates.ReplaceVersionFileAsync(id, versionId, file, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/rescan-placeholders")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> RescanPlaceholders(Guid id, Guid versionId, CancellationToken ct)
    {
        await templates.RefreshVersionAfterExternalEditAsync(id, versionId, ct);
        var item = await templates.GetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/insert-placeholder")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> InsertPlaceholder(
        Guid id,
        Guid versionId,
        [FromBody] InsertTemplatePlaceholderRequest req,
        CancellationToken ct)
    {
        try
        {
            var item = await templates.InsertPlaceholderAsync(id, versionId, req.Key, req.ParagraphIndex, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/versions/{versionId:guid}/file")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> DownloadVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var file = await templates.GetVersionFileAsync(id, versionId, ct);
        if (file is null)
            return NotFound();

        return PhysicalFile(
            file.Value.fullPath,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            file.Value.fileName);
    }

    private bool TryUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}

public class UploadContractTemplateVersionForm
{
    public IFormFile File { get; set; } = default!;
    public string? ChangeNote { get; set; }
}
