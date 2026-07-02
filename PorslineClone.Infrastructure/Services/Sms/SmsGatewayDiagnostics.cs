using Microsoft.Extensions.Options;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Auth;

namespace PorslineClone.Infrastructure.Services.Sms;

public sealed class SmsGatewayDiagnostics(IOptions<SmsGatewayOptions> options)
{
    public SmsGatewayStatusDto GetStatus()
    {
        var cfg = options.Value;
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(cfg.UrlAddress))
            issues.Add("آدرس درگاه (UrlAddress) تنظیم نشده است");
        else if (!Uri.TryCreate(cfg.UrlAddress.Trim(), UriKind.Absolute, out _))
            issues.Add("آدرس درگاه معتبر نیست");

        if (string.IsNullOrWhiteSpace(cfg.CallerId))
            issues.Add("Caller-ID تنظیم نشده است");

        var passwordOk = !string.IsNullOrWhiteSpace(cfg.Password)
            && !string.Equals(cfg.Password.Trim(), "CHANGE_ME", StringComparison.OrdinalIgnoreCase);
        if (!passwordOk)
            issues.Add("رمز درگاه (Password) تنظیم نشده یا مقدار پیش‌فرض است");

        return new SmsGatewayStatusDto(
            issues.Count == 0,
            string.IsNullOrWhiteSpace(cfg.UrlAddress) ? null : cfg.UrlAddress.Trim(),
            string.IsNullOrWhiteSpace(cfg.CallerId) ? null : cfg.CallerId.Trim(),
            passwordOk,
            issues);
    }
}
