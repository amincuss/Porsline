namespace PorslineClone.Domain.Entities;

/// <summary>
/// تنظیمات تک‌ردیفی برای آدرس‌های پایهٔ فرانت (لینک‌های پیامک و غیره).
/// </summary>
public class SiteSettings
{
    public int Id { get; set; } = 1;

    /// <summary>دامنهٔ عمومی برای لینک‌های فرم (مثلاً تکمیل فرم توسط پاسخگو).</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>دامنهٔ پنل مدیریت برای لینک ورود و تأییدیه‌ها؛ در صورت خالی بودن از PublicBaseUrl و سپس appsettings استفاده می‌شود.</summary>
    public string? AdminPanelBaseUrl { get; set; }
}
