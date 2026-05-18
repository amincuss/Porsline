namespace PorslineClone.Infrastructure.Options;

public class ContractSignatureOptions
{
    public const string SectionName = "ContractSignatures";

    /// <summary>فعال‌سازی درج امضا روی فایل قرارداد</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>مسیر اجرایی LibreOffice برای تبدیل DOCX به PDF (اختیاری)</summary>
    public string? LibreOfficePath { get; set; }

    /// <summary>اگر true و LibreOffice نبود، فقط DOCX امضاشده ذخیره می‌شود</summary>
    public bool AllowDocxWithoutPdfConversion { get; set; } = true;
}
