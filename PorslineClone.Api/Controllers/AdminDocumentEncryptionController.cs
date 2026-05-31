using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-encryption")]
[Authorize(Policy = "forms.update")]
public class AdminDocumentEncryptionController(
    IDocumentEncryptionKeyRotationService rotation,
    DocumentEnvelopeEncryptionService encryption) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            enabled = encryption.IsEncryptionActive,
            algorithm = DocumentEnvelopeEncryptionService.AlgorithmName,
            primaryKeyId = encryption.PrimaryKeyId,
        });
    }

    /// <summary>DEKهای موجود را با KEK فعال (Primary) دوباره می‌پیچد — فایل روی دیسک تغییر نمی‌کند.</summary>
    [HttpPost("rotate-dek-wrappers")]
    public async Task<IActionResult> RotateDekWrappers(
        [FromQuery] Guid? documentId,
        [FromQuery] int batchSize = 200,
        CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 2000)
            return BadRequest(new { message = "batchSize باید بین ۱ و ۲۰۰۰ باشد" });

        var result = await rotation.RotateDekWrappersAsync(documentId, batchSize, ct);
        return Ok(new
        {
            message = "چرخش wrapper کلید DEK انجام شد",
            result.Scanned,
            result.Rotated,
            result.Skipped,
            result.Failed,
            result.PrimaryKeyId,
        });
    }
}
