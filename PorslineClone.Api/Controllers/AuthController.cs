using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, IWebHostEnvironment env, AppDbContext db) : ControllerBase
{
    /// <summary>تنظیم روش ورود — عمومی، بدون احراز هویت</summary>
    [HttpGet("login-config")]
    public async Task<IActionResult> LoginConfig(CancellationToken cancellationToken)
    {
        var settings = await db.SecuritySettings.FirstOrDefaultAsync(cancellationToken) ?? new SecuritySettings();
        return Ok(new LoginConfigDto(settings.LoginMethod.ToString()));
    }

    [HttpPost("login/password")]
    public async Task<IActionResult> LoginWithPassword([FromBody] PasswordLoginDto dto, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await authService.LoginWithPasswordAsync(dto.MobileNumber, dto.Password, ip, cancellationToken);
        return result is null
            ? Unauthorized(new { message = "شماره موبایل یا رمز عبور نادرست است" })
            : Ok(result);
    }

    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] OtpRequestDto request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var sendResult = await authService.SendOtpAsync(request.MobileNumber, ip, cancellationToken);

        if (env.IsDevelopment())
        {
            return Ok(new
            {
                message = "اگر شماره معتبر باشد، کد تایید ارسال می‌شود",
                otpCode = sendResult.OtpCode
            });
        }

        return Ok(new { message = "اگر شماره معتبر باشد، کد تایید ارسال می‌شود" });
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyDto request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await authService.VerifyOtpAsync(request.MobileNumber, request.Code, ip, cancellationToken);
        return result is null ? Unauthorized(new { message = "اطلاعات ورود نامعتبر است" }) : Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
        return result is null ? Unauthorized(new { message = "refresh token معتبر نیست" }) : Ok(result);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var ok = await authService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return ok ? Ok(new { message = "refresh token با موفقیت باطل شد" }) : NotFound(new { message = "refresh token پیدا نشد" });
    }
}
