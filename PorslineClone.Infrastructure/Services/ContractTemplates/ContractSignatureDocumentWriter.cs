using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PorslineClone.Application.ContractTemplates;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

public sealed record ContractSignatureSlot(
    int WorkflowOrder,
    string PlaceholderKey,
    byte[] ImageBytes,
    string ImageExtension,
    string ApproverFullName,
    string? PositionTitle);

/// <summary>
/// هر امضا فقط در placeholder با همان کلید (مثلاً sign_1 برای مرحله ۱) — بدون جایگزینی تصادفی.
/// </summary>
public static class ContractSignatureDocumentWriter
{
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> ScanPlaceholderKeys(string docxFullPath)
    {
        if (!File.Exists(docxFullPath))
            return [];

        using var doc = WordprocessingDocument.Open(docxFullPath, false);
        var part = doc.MainDocumentPart;
        if (part is null)
            return [];

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in DiscoverPlaceholderHits(part))
        {
            if (seen.Add(hit.Key))
                keys.Add(hit.Key);
        }

        return keys;
    }

    public static SignatureApplyResult ApplySignatures(string docxFullPath, IReadOnlyList<ContractSignatureSlot> slots)
    {
        if (slots.Count == 0 || !File.Exists(docxFullPath))
            return new SignatureApplyResult(0, []);

        var orderedSlots = slots.OrderBy(s => s.WorkflowOrder).ToList();
        var usedMarkers = new HashSet<string>(StringComparer.Ordinal);

        WordOpenXmlImageHelper.ResetDrawingIds();
        using var doc = WordprocessingDocument.Open(docxFullPath, true);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is null)
            return new SignatureApplyResult(0, ScanPlaceholderKeys(docxFullPath));

        var inserted = 0;
        var missingKeys = new List<string>();

        foreach (var slot in orderedSlots)
        {
            var slotKey = ContractTemplateSystemFields.NormalizeKey(slot.PlaceholderKey);
            if (slotKey.Length == 0)
                continue;

            if (TryReplaceExactPlaceholderKey(part, slot, slotKey, usedMarkers))
                inserted++;
            else
                missingKeys.Add(slotKey);
        }

        part.Document.Save();
        return new SignatureApplyResult(inserted, missingKeys);
    }

    public sealed record SignatureApplyResult(int InsertedInPlaceholder, IReadOnlyList<string> MissingKeys);

    /// <summary>فقط placeholder با کلید دقیق slot — اولین مورد در ترتیب خواندن سند.</summary>
    private static bool TryReplaceExactPlaceholderKey(
        MainDocumentPart mainPart,
        ContractSignatureSlot slot,
        string exactKey,
        HashSet<string> usedMarkers)
    {
        foreach (var paragraph in EnumerateParagraphsInDocumentOrder(mainPart))
        {
            PlaceholderParagraphHelper.CollapseParagraphText(paragraph);

            var raw = PlaceholderParagraphHelper.GetParagraphText(paragraph);
            if (string.IsNullOrEmpty(raw))
                continue;

            var (normalized, normToRaw) = PlaceholderParagraphHelper.BuildNormalizedWithIndexMap(raw);
            if (string.IsNullOrEmpty(normalized))
                continue;

            foreach (Match match in PlaceholderRegex.Matches(normalized).Cast<Match>().OrderBy(m => m.Index))
            {
                var key = ContractTemplateSystemFields.NormalizeKey(match.Groups[1].Value.Trim());
                if (!key.Equals(exactKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var marker = BuildMarker(paragraph, match.Index);
                if (!usedMarkers.Add(marker))
                    continue;

                try
                {
                    var ext = string.IsNullOrWhiteSpace(slot.ImageExtension) ? ".png" : slot.ImageExtension;
                    if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                        return false;

                    var (rawStart, rawEnd) = PlaceholderParagraphHelper.MapNormalizedRangeToRaw(
                        normToRaw, match.Index, match.Length);

                    if (ParagraphImageInserter.InsertSignatureAtRawRangePublic(
                            mainPart,
                            paragraph,
                            rawStart,
                            rawEnd,
                            slot.ImageBytes,
                            ext,
                            widthPx: 140,
                            $"{exactKey}_{slot.WorkflowOrder}",
                            slot.ApproverFullName,
                            slot.PositionTitle))
                        return true;

                    usedMarkers.Remove(marker);
                }
                catch
                {
                    usedMarkers.Remove(marker);
                }
            }
        }

        return false;
    }

    private static string BuildMarker(Paragraph paragraph, int normIndex)
        => $"{paragraph.GetHashCode()}:{normIndex}";

    private sealed record PlaceholderHit(string Key, int Sequence);

    private static IEnumerable<PlaceholderHit> DiscoverPlaceholderHits(MainDocumentPart mainPart)
    {
        var seq = 0;
        foreach (var paragraph in EnumerateParagraphsInDocumentOrder(mainPart))
        {
            PlaceholderParagraphHelper.CollapseParagraphText(paragraph);
            var text = PlaceholderParagraphHelper.NormalizeForPlaceholderMatch(
                PlaceholderParagraphHelper.GetParagraphText(paragraph));
            foreach (Match m in PlaceholderRegex.Matches(text))
            {
                var k = ContractTemplateSystemFields.NormalizeKey(m.Groups[1].Value.Trim());
                if (k.Length > 0)
                    yield return new PlaceholderHit(k, seq++);
            }
        }
    }

    /// <summary>ترتیب خواندن: body → header → footer (نه DFS تصادفی روی کل descendants).</summary>
    private static IEnumerable<Paragraph> EnumerateParagraphsInDocumentOrder(MainDocumentPart mainPart)
    {
        if (mainPart.Document?.Body is { } body)
            foreach (var p in WalkParagraphsInOrder(body))
                yield return p;

        foreach (var header in mainPart.HeaderParts)
            if (header.Header is not null)
                foreach (var p in WalkParagraphsInOrder(header.Header))
                    yield return p;

        foreach (var footer in mainPart.FooterParts)
            if (footer.Footer is not null)
                foreach (var p in WalkParagraphsInOrder(footer.Footer))
                    yield return p;

        foreach (var fnPart in mainPart.GetPartsOfType<FootnotesPart>())
            if (fnPart.Footnotes is not null)
                foreach (var p in WalkParagraphsInOrder(fnPart.Footnotes))
                    yield return p;

        foreach (var enPart in mainPart.GetPartsOfType<EndnotesPart>())
            if (enPart.Endnotes is not null)
                foreach (var p in WalkParagraphsInOrder(enPart.Endnotes))
                    yield return p;
    }

    private static IEnumerable<Paragraph> WalkParagraphsInOrder(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table:
                    foreach (var row in table.Elements<TableRow>())
                    {
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            foreach (var p in WalkParagraphsInOrder(cell))
                                yield return p;
                        }
                    }
                    break;
                default:
                    if (child.HasChildren)
                    {
                        foreach (var p in WalkParagraphsInOrder(child))
                            yield return p;
                    }
                    break;
            }
        }
    }
}
