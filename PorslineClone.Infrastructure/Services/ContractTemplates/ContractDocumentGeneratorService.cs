using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.ContractTemplates;

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

        ScanInContainer(part.Document.Body, found);

        foreach (var headerPart in part.HeaderParts)
            if (headerPart.Header is not null)
                ScanInContainer(headerPart.Header, found);

        foreach (var footerPart in part.FooterParts)
            if (footerPart.Footer is not null)
                ScanInContainer(footerPart.Footer, found);

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

        var lookup = BuildValueLookup(fieldValues);
        await Task.Run(() => ReplaceInDocument(tempPath, lookup), ct);
        return tempPath;
    }

    private static void ScanInContainer(OpenXmlElement container, HashSet<string> found)
    {
        foreach (var paragraph in EnumerateContentParagraphs(container))
        {
            var combined = PlaceholderParagraphHelper.GetParagraphText(paragraph);
            ExtractFromText(combined, found);
        }
    }

    private static void ReplaceInDocument(string docxPath, IReadOnlyDictionary<string, string> fieldValues)
    {
        WordOpenXmlImageHelper.ResetDrawingIds();
        using var doc = WordprocessingDocument.Open(docxPath, true);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is null)
            return;

        ReplaceInContainer(part, part.Document.Body, fieldValues);

        foreach (var headerPart in part.HeaderParts)
            if (headerPart.Header is not null)
                ReplaceInContainer(part, headerPart.Header, fieldValues);

        foreach (var footerPart in part.FooterParts)
            if (footerPart.Footer is not null)
                ReplaceInContainer(part, footerPart.Footer, fieldValues);

        part.Document.Save();
    }

    private static IEnumerable<Paragraph> EnumerateContentParagraphs(OpenXmlElement container)
        => container.Descendants<Paragraph>();

    private static void ReplaceInContainer(
        MainDocumentPart mainPart,
        OpenXmlElement container,
        IReadOnlyDictionary<string, string> fieldValues)
    {
        foreach (var paragraph in EnumerateContentParagraphs(container).ToList())
            ReplaceInParagraph(mainPart, paragraph, fieldValues);
    }

    private static void ReplaceInParagraph(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        IReadOnlyDictionary<string, string> fieldValues)
    {
        PlaceholderParagraphHelper.CollapseParagraphText(paragraph);

        var combined = PlaceholderParagraphHelper.NormalizeForPlaceholderMatch(
            PlaceholderParagraphHelper.GetParagraphText(paragraph));

        if (string.IsNullOrEmpty(combined))
            return;

        // ۱) تصاویر در همان محل placeholder (inline)
        ParagraphImageInserter.TryInsertAllImagePlaceholders(
            mainPart, paragraph, fieldValues, PlaceholderRegex);

        // ۲) متن — بعد از درج تصویر دوباره متن را بخوان
        combined = PlaceholderParagraphHelper.NormalizeForPlaceholderMatch(
            PlaceholderParagraphHelper.GetParagraphText(paragraph));

        if (string.IsNullOrEmpty(combined))
            return;

        var updated = PlaceholderRegex.Replace(combined, match =>
        {
            var rawKey = match.Groups[1].Value.Trim();
            if (!TryResolveValue(rawKey, fieldValues, out var value))
                return match.Value;

            if (ContractTemplateImageValue.TryParse(UnwrapStoredFieldValue(value), out _))
                return "";

            return value ?? "";
        });

        if (updated == combined)
            return;

        ApplyTextToParagraph(paragraph, updated);
    }

    private static void ApplyTextToParagraph(Paragraph paragraph, string text)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            paragraph.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            return;
        }

        texts[0].Text = text;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }

    internal static bool TryResolveValuePublic(
        string rawPlaceholderKey,
        IReadOnlyDictionary<string, string> fieldValues,
        out string value) => TryResolveValue(rawPlaceholderKey, fieldValues, out value);

    internal static string UnwrapStoredFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var v = value.Trim();
        if (v.StartsWith('{') || v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return v;

        if (v.StartsWith('"') && v.EndsWith('"'))
        {
            try
            {
                var inner = JsonSerializer.Deserialize<string>(v);
                if (!string.IsNullOrWhiteSpace(inner))
                    return inner.Trim();
            }
            catch
            {
                // ignore
            }
        }

        return v;
    }

    public void InsertPlaceholder(string docxFullPath, string key, int paragraphIndex)
    {
        if (!File.Exists(docxFullPath))
            throw new FileNotFoundException("فایل قالب یافت نشد", docxFullPath);

        var normalizedKey = NormalizePlaceholderKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            throw new ArgumentException("کلید فیلد نامعتبر است", nameof(key));

        var token = "{{" + normalizedKey + "}}";

        using var doc = WordprocessingDocument.Open(docxFullPath, true);
        var body = doc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("بدنه سند Word یافت نشد");

        var paragraphs = EnumerateContentParagraphs(body).ToList();
        if (paragraphs.Count == 0)
        {
            body.AppendChild(new Paragraph(new Run(new Text(token) { Space = SpaceProcessingModeValues.Preserve })));
        }
        else
        {
            var idx = Math.Clamp(paragraphIndex, 0, paragraphs.Count - 1);
            var target = paragraphs[idx];
            var existing = PlaceholderParagraphHelper.GetParagraphText(target);
            var prefix = string.IsNullOrEmpty(existing) || existing.EndsWith(' ') || existing.EndsWith('\t')
                ? ""
                : " ";
            var run = target.Descendants<Run>().LastOrDefault();
            if (run is not null)
                run.AppendChild(new Text(prefix + token) { Space = SpaceProcessingModeValues.Preserve });
            else
                target.AppendChild(new Run(new Text(prefix + token) { Space = SpaceProcessingModeValues.Preserve }));
        }

        doc.MainDocumentPart!.Document!.Save();
    }

    private static void ExtractFromText(string? text, HashSet<string> found)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var normalized = PlaceholderParagraphHelper.NormalizeForPlaceholderMatch(text);
        foreach (Match m in PlaceholderRegex.Matches(normalized))
        {
            var key = NormalizePlaceholderKey(m.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(key))
                found.Add(key);
        }
    }

    private static Dictionary<string, string> BuildValueLookup(IReadOnlyDictionary<string, string> fieldValues)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in fieldValues)
        {
            var value = UnwrapStoredFieldValue(kv.Value);
            if (!string.IsNullOrWhiteSpace(kv.Key))
                lookup[kv.Key.Trim()] = value;

            var norm = NormalizePlaceholderKey(kv.Key);
            if (!string.IsNullOrWhiteSpace(norm))
                lookup[norm] = value;
        }

        return lookup;
    }

    private static bool TryResolveValue(
        string rawPlaceholderKey,
        IReadOnlyDictionary<string, string> fieldValues,
        out string value)
    {
        value = "";
        if (fieldValues.TryGetValue(rawPlaceholderKey, out var direct) && direct is not null)
        {
            value = direct;
            return true;
        }

        var norm = NormalizePlaceholderKey(rawPlaceholderKey);
        if (!string.IsNullOrWhiteSpace(norm) && fieldValues.TryGetValue(norm, out var byNorm) && byNorm is not null)
        {
            value = byNorm;
            return true;
        }

        return false;
    }

    private static string NormalizePlaceholderKey(string key)
        => new string((key ?? "").Trim().Where(static ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray())
            .ToLowerInvariant();

    [GeneratedRegex(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();
}
