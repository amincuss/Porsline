using PorslineClone.Application.Contracts;

namespace PorslineClone.Application.Abstractions;

public interface ISmsSender
{
    Task<bool> SendSmsAsync(SmsRequest smsRequest, CancellationToken cancellationToken = default);
}

public interface IAuthService
{
    Task<OtpSendResultDto> SendOtpAsync(string mobileNumber, string ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> VerifyOtpAsync(string mobileNumber, string code, string ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> LoginWithPasswordAsync(string mobileNumber, string password, string ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
