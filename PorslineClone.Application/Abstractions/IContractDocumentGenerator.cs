namespace PorslineClone.Application.Abstractions;

public interface IContractDocumentGenerator
{
    /// <summary>جایگزینی {{key}} در فایل docx و برگرداندن مسیر فایل موقت</summary>
    Task<string> GenerateDocxAsync(
        string sourceDocxFullPath,
        IReadOnlyDictionary<string, string> fieldValues,
        CancellationToken ct = default);

    IReadOnlyList<string> ScanPlaceholders(string docxFullPath);
}
