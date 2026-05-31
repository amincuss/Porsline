namespace PorslineClone.Application.Abstractions;

public interface ILibreOfficeDocumentService
{
    bool IsAvailable { get; }

    /// <summary>تبدیل سند آفیس/PDF به PDF با LibreOffice.</summary>
    string? TryConvertToPdf(string inputFullPath);

    /// <summary>استخراج متن ساده برای مقایسه نسخه‌ها (docx, xlsx, pdf, txt).</summary>
    string? TryExtractPlainText(string inputFullPath);
}
