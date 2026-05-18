using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

public partial class ContractDocumentGeneratorService : IContractDocumentGenerator
{
    private static readonly Regex PlaceholderRegex = PlaceholderPattern();

    public IReadOnlyList<string> ScanPlaceholders(string docxFullPath)
    {
        if (!File.Exists(docxFullPath))
            return [];

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = WordprocessingDocument.Open(docxFullPath, false);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is null)
            return [];

        foreach (var text in part.Document.Body.Descendants<Text>())
            ExtractFromText(text.Text, found);

        foreach (var headerPart in part.HeaderParts)
            foreach (var text in headerPart.Header?.Descendants<Text>() ?? [])
                ExtractFromText(text.Text, found);

        foreach (var footerPart in part.FooterParts)
            foreach (var text in footerPart.Footer?.Descendants<Text>() ?? [])
                ExtractFromText(text.Text, found);

        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string> GenerateDocxAsync(
        string sourceDocxFullPath,
        IReadOnlyDictionary<string, string> fieldValues,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceDocxFullPath))
            throw new FileNotFoundException("فایل قالب یافت نشد", sourceDocxFullPath);

        var tempPath = Path.Combine(Path.GetTempPath(), $"contract-gen-{Guid.NewGuid():N}.docx");
        File.Copy(sourceDocxFullPath, tempPath, overwrite: true);

        await Task.Run(() => ReplaceInDocument(tempPath, fieldValues), ct);
        return tempPath;
    }

    private static void ReplaceInDocument(string docxPath, IReadOnlyDictionary<string, string> fieldValues)
    {
        using var doc = WordprocessingDocument.Open(docxPath, true);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is null)
            return;

        ReplaceInContainer(part.Document.Body, fieldValues);

        foreach (var headerPart in part.HeaderParts)
            if (headerPart.Header is not null)
                ReplaceInContainer(headerPart.Header, fieldValues);

        foreach (var footerPart in part.FooterParts)
            if (footerPart.Footer is not null)
                ReplaceInContainer(footerPart.Footer, fieldValues);

        part.Document.Save();
    }

    private static void ReplaceInContainer(OpenXmlElement container, IReadOnlyDictionary<string, string> fieldValues)
    {
        foreach (var paragraph in container.Descendants<Paragraph>())
            ReplaceInParagraph(paragraph, fieldValues);
    }

    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> fieldValues)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0)
            return;

        var combined = string.Concat(texts.Select(t => t.Text));
        var updated = combined;
        foreach (var kv in fieldValues)
        {
            var token = $"{{{{{kv.Key}}}}}";
            if (updated.Contains(token, StringComparison.Ordinal))
                updated = updated.Replace(token, kv.Value ?? "", StringComparison.Ordinal);
        }

        if (updated == combined)
            return;

        texts[0].Text = updated;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }

    private static void ExtractFromText(string? text, HashSet<string> found)
    {
        if (string.IsNullOrEmpty(text))
            return;
        foreach (Match m in PlaceholderRegex.Matches(text))
        {
            var key = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(key))
                found.Add(key);
        }
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();
}
