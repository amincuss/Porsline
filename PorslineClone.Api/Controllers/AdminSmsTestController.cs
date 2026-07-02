using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Services.Sms;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings/sms-test")]
[Authorize]
public class AdminSmsTestController(SmsTestService smsTest) : ControllerBase
{
    [HttpGet("status")]
    [Authorize(Policy = "settings.sms.test")]
    public IActionResult Status() => Ok(smsTest.GetGatewayStatus());

    [HttpGet("patterns")]
    [Authorize(Policy = "settings.sms.test")]
    public async Task<IActionResult> Patterns(CancellationToken ct) =>
        Ok(new { patterns = await smsTest.GetPatternOptionsAsync(ct) });

    [HttpPost("preview")]
    [Authorize(Policy = "settings.sms.test")]
    public async Task<IActionResult> Preview([FromBody] SmsTestPreviewRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await smsTest.PreviewAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("send")]
    [Authorize(Policy = "settings.sms.test")]
    public async Task<IActionResult> Send([FromBody] SmsTestSendRequest request, CancellationToken ct) =>
        Ok(await smsTest.SendAsync(request, ct));
}
