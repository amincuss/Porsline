using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;

namespace PorslineClone.Infrastructure.Services.Sms;

/// <summary>ارسال پیامک — اعتبارسنجی، throttle، درگاه، لاگ.</summary>
public sealed class SmsSender(
    EntekhabSmsGatewayClient gateway,
    ISmsLogService smsLogs,
    ILogger<SmsSender> logger) : ISmsSender
{
    private const int MaxMessageLength = 536;

    public Task<bool> SendSmsAsync(SmsRequest smsRequest, CancellationToken cancellationToken = default) =>
        smsRequest.SkipThrottle
            ? SendOnceAsync(smsRequest, cancellationToken)
            : SmsGatewayThrottle.RunAsync(() => SendOnceAsync(smsRequest, cancellationToken), cancellationToken);

    private async Task<bool> SendOnceAsync(SmsRequest smsRequest, CancellationToken cancellationToken)
    {
        var isResend = smsRequest.UpdateExistingLogId is Guid;
        var mobile = UserFieldNormalizer.NormalizeMobile(smsRequest.MobileNumber);
        var message = PrepareMessage(smsRequest.Message);

        if (!UserFieldNormalizer.IsValidMobile(mobile))
        {
            await PersistLogAsync(mobile, message, false, "شماره موبایل معتبر نیست",
                $"Invalid mobile: {smsRequest.MobileNumber}", smsRequest, null, isResend, cancellationToken);
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            await PersistLogAsync(mobile, message, false, "متن پیامک خالی است",
                "Empty message body", smsRequest, null, isResend, cancellationToken);
            return false;
        }

        logger.LogInformation("[SMS] Sending to {Mobile} source={Source} resend={Resend}",
            mobile, smsRequest.Source, isResend);

        var gatewayResult = await gateway.SendAsync(mobile, message, cancellationToken);

        await PersistLogAsync(
            mobile,
            message,
            gatewayResult.IsSuccess,
            gatewayResult.IsSuccess ? null : gatewayResult.ErrorMessage ?? SmsLogMessages.Unexpected(),
            gatewayResult.IsSuccess ? null : gatewayResult.RawBody,
            smsRequest,
            gatewayResult.HttpStatusCode,
            isResend,
            cancellationToken);

        return gatewayResult.IsSuccess;
    }

    private static string PrepareMessage(string? raw)
    {
        var message = (raw ?? "").Trim();
        return message.Length > MaxMessageLength ? message[..MaxMessageLength] : message;
    }

    private Task PersistLogAsync(
        string mobile,
        string message,
        bool isSuccess,
        string? errorMessage,
        string? technicalDetail,
        SmsRequest request,
        int? httpStatusCode,
        bool isResend,
        CancellationToken cancellationToken)
    {
        var entry = new SmsLogEntry(
            mobile,
            message,
            isSuccess,
            errorMessage,
            technicalDetail,
            request.Source,
            httpStatusCode);

        if (isResend && request.UpdateExistingLogId is Guid logId)
            return smsLogs.UpdateAsync(logId, entry, cancellationToken);

        return smsLogs.LogAsync(entry, cancellationToken);
    }
}
