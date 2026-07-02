using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using PorslineClone.Infrastructure.Auth;

namespace PorslineClone.Infrastructure.Services.Sms;

public sealed record EntekhabGatewayResponse(
    bool IsSuccess,
    int? HttpStatusCode,
    string? RawBody,
    string? ErrorMessage);

/// <summary>
/// کلاینت درگاه entekhab — هر SMS یک HttpRequestMessage جدا؛ هیچ propertyای روی HttpClient مشترک تغییر نمی‌کند.
/// </summary>
public sealed class EntekhabSmsGatewayClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsGatewayOptions> options,
    ILogger<EntekhabSmsGatewayClient> logger)
{
    public const string HttpClientName = "EntekhabSms";

    private static readonly SemaphoreSlim SendGate = new(1, 1);

    public async Task<EntekhabGatewayResponse> SendAsync(
        string mobile,
        string message,
        CancellationToken cancellationToken = default)
    {
        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.UrlAddress)
            || string.IsNullOrWhiteSpace(cfg.CallerId)
            || string.IsNullOrWhiteSpace(cfg.Password))
        {
            return new EntekhabGatewayResponse(
                false,
                null,
                null,
                "تنظیمات درگاه پیامک ناقص است");
        }

        await SendGate.WaitAsync(cancellationToken);
        try
        {
            return await SendCoreAsync(mobile, message, cfg, cancellationToken);
        }
        finally
        {
            SendGate.Release();
        }
    }

    private async Task<EntekhabGatewayResponse> SendCoreAsync(
        string mobile,
        string message,
        SmsGatewayOptions cfg,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(cfg.UrlAddress.Trim(), UriKind.Absolute, out var requestUri))
            {
                return new EntekhabGatewayResponse(
                    false,
                    null,
                    null,
                    "آدرس درگاه پیامک معتبر نیست");
            }

            var client = httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.TryAddWithoutValidation("Caller-ID", cfg.CallerId);
            request.Headers.TryAddWithoutValidation("Password", cfg.Password);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("Mobile", mobile),
                new KeyValuePair<string, string>("Message", message),
                new KeyValuePair<string, string>("Priority", "0"),
                new KeyValuePair<string, string>("Provider", "3"),
            ]);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var httpCode = (int)response.StatusCode;
            var parsed = JsonConvert.DeserializeObject<EntekhabSmsResult>(body);
            var gatewayOk = parsed?.IsSuccess ?? false;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[SMS] HTTP {Code} for {Mobile}: {Body}", httpCode, mobile, body);
                return new EntekhabGatewayResponse(false, httpCode, body, SmsLogMessages.HttpError(httpCode));
            }

            if (!gatewayOk)
            {
                logger.LogWarning("[SMS] Gateway rejected {Mobile}: {Body}", mobile, body);
                return new EntekhabGatewayResponse(false, httpCode, body, SmsLogMessages.GatewayRejected());
            }

            return new EntekhabGatewayResponse(true, httpCode, body, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SMS] Connection failed for {Mobile}", mobile);
            return new EntekhabGatewayResponse(false, null, ex.Message, SmsLogMessages.ConnectionFailed());
        }
    }

    private sealed class EntekhabSmsResult
    {
        public bool IsSuccess { get; set; }
    }
}
