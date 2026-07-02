using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings/sms-patterns")]
[Authorize]
public class AdminSmsPatternsController(ISmsPatternService patterns) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await patterns.EnsureSeededAsync(ct);
        var grouped = await patterns.GetGroupedAsync(ct);
        return Ok(new { categories = grouped });
    }

    [HttpPut]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> Update([FromBody] UpdateSmsPatternsRequest req, CancellationToken ct)
    {
        if (req.Patterns is null || req.Patterns.Count == 0)
            return BadRequest(new { message = "هیچ پترنی برای ذخیره ارسال نشده" });

        await patterns.UpdateTemplatesAsync(req.Patterns, ct);
        return Ok(new { message = "پترن‌های پیامک ذخیره شد" });
    }

    [HttpPost("{key}/reset")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> Reset(string key, CancellationToken ct)
    {
        try
        {
            await patterns.ResetToDefaultAsync(key, ct);
            var grouped = await patterns.GetGroupedAsync(ct);
            return Ok(new { message = "پترن به حالت پیش‌فرض بازگردانده شد", categories = grouped });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
