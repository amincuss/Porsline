using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Auth;

namespace PorslineClone.Infrastructure.Services;

public class SmsSender(HttpClient httpClient, IOptions<SmsGatewayOptions> options, ILogger<SmsSender> logger) : ISmsSender
{
    public async Task<bool> SendSmsAsync(SmsRequest smsRequest, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("[SMS] To: {Mobile} | Message: {Message}", smsRequest.MobileNumber, smsRequest.Message);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            var cfg = options.Value;
            var baseUri = new Uri(cfg.UrlAddress);
            httpClient.BaseAddress = baseUri;
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.ConnectionClose = true;
            httpClient.DefaultRequestHeaders.Remove("Caller-ID");
            httpClient.DefaultRequestHeaders.Remove("Password");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Caller-ID", cfg.CallerId);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Password", cfg.Password);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var formContent = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("Mobile", smsRequest.MobileNumber),
                new KeyValuePair<string, string>("Message", smsRequest.Message),
                new KeyValuePair<string, string>("Priority", "0"),
                new KeyValuePair<string, string>("Provider", "1")
            ]);

            var res = await httpClient.PostAsync(baseUri, formContent, cancellationToken);
            var content = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode) return false;
            return ParseSmsSuccess(content);
        }
        catch
        {
            return false;
        }
    }

    private static bool ParseSmsSuccess(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        // Common gateway responses:
        // { "IsSuccess": true }, { "isSuccess": true }, true, "true", { "Status": 1 }
        if (bool.TryParse(content.Trim('"', ' ', '\n', '\r', '\t'), out var boolValue))
        {
            return boolValue;
        }

        try
        {
            var jToken = JToken.Parse(content);

            if (jToken.Type == JTokenType.Boolean)
                return jToken.Value<bool>();

            if (jToken is JObject obj)
            {
                var successToken = obj.GetValue("IsSuccess", StringComparison.OrdinalIgnoreCase)
                                   ?? obj.GetValue("Success", StringComparison.OrdinalIgnoreCase);
                if (successToken?.Type == JTokenType.Boolean)
                    return successToken.Value<bool>();

                var statusToken = obj.GetValue("Status", StringComparison.OrdinalIgnoreCase)
                                 ?? obj.GetValue("Code", StringComparison.OrdinalIgnoreCase);
                if (statusToken is not null && int.TryParse(statusToken.ToString(), out var status))
                    return status == 1 || status == 200 || status == 0;
            }
        }
        catch
        {
            // ignore parse errors and return false
        }

        return false;
    }
}
