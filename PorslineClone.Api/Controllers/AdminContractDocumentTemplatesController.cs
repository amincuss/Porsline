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
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadVersion(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] string? changeNote,
        CancellationToken ct)
    {
        if (!TryUserId(out var userId))
            return Unauthorized();
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل قالب الزامی است" });

        try
        {
            var item = await templates.UploadVersionAsync(id, file, changeNote, userId, ct);
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
        var item = await templates.SaveFieldsAsync(id, req, ct);
        return item is null ? NotFound() : Ok(item);
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
