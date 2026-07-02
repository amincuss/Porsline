namespace PorslineClone.Domain.Entities;

/// <summary>لاگ ارسال پیامک از درگاه.</summary>
public class SmsLog
{
    public Guid Id { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    /// <summary>پیام خطا برای نمایش به کاربر (فارسی).</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>جزئیات فنی — فقط در جزئیات لاگ.</summary>
    public string? TechnicalDetail { get; set; }
    /// <summary>منبع/زمینه ارسال (اختیاری).</summary>
    public string? Source { get; set; }
    public int? HttpStatusCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
