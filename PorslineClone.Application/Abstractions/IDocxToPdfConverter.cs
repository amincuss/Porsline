namespace PorslineClone.Application.Abstractions;

public interface IDocxToPdfConverter
{
    /// <summary>تبدیل DOCX به PDF با LibreOffice. در صورت نبود LibreOffice null برمی‌گرداند.</summary>
    string? TryConvert(string docxFullPath);

    bool IsAvailable { get; }
}
