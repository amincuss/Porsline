using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PorslineClone.Application.ContractTemplates;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

/// <summary>درج تصویر دقیقاً در محل placeholder داخل همان پاراگراف (inline).</summary>
internal static class ParagraphImageInserter
{
    private sealed class TextSpan
    {
        public required Text Node { get; init; }
        public int Start { get; init; }
        public int End { get; init; }
    }

    public static bool TryInsertAllImagePlaceholders(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        IReadOnlyDictionary<string, string> fieldValues,
        Regex placeholderRegex)
    {
        var any = false;
        var safety = 0;

        while (safety++ < 32)
        {
            var raw = PlaceholderParagraphHelper.GetParagraphText(paragraph);
            var (normalized, normToRaw) = PlaceholderParagraphHelper.BuildNormalizedWithIndexMap(raw);

            var match = placeholderRegex.Matches(normalized)
                .Cast<Match>()
                .Where(m => m.Success && TryResolveImagePlaceholder(m.Groups[1].Value.Trim(), fieldValues, out _))
                .OrderByDescending(m => m.Index)
                .FirstOrDefault();

            if (match is null)
                break;

            var key = match.Groups[1].Value.Trim();
            if (!TryResolveImagePlaceholder(key, fieldValues, out var payload) || payload is null)
                break;

            try
            {
                var (bytes, ext) = ContractTemplateImageValue.DecodeDataUrl(payload.DataUrl);
                if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                    break;

                var (rawStart, rawEnd) = PlaceholderParagraphHelper.MapNormalizedRangeToRaw(
                    normToRaw, match.Index, match.Length);

                if (TryInsertImageAtRawRange(mainPart, paragraph, rawStart, rawEnd, bytes, ext, payload.WidthPx, key, caption: null))
                    any = true;
                else
                    break;
            }
            catch
            {
                break;
            }
        }

        return any;
    }

    private static bool TryResolveImagePlaceholder(
        string key,
        IReadOnlyDictionary<string, string> fieldValues,
        out ContractTemplateImagePayload? payload)
    {
        payload = null;
        if (!ContractDocumentGeneratorService.TryResolveValuePublic(key, fieldValues, out var rawValue))
            return false;

        return ContractTemplateImageValue.TryParse(
            ContractDocumentGeneratorService.UnwrapStoredFieldValue(rawValue),
            out payload);
    }

    /// <summary>درج تصویر در بازه متنی خام پاراگراف (برای امضا و تصویر قالب).</summary>
    public static bool InsertImageAtRawRangePublic(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        int rawRangeStart,
        int rawRangeEnd,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName)
        => TryInsertImageAtRawRange(
            mainPart, paragraph, rawRangeStart, rawRangeEnd,
            imageBytes, fileExtension, widthPx, imageName, caption: null);

    /// <summary>درج امضا + نام و سمت تأییدکننده زیر تصویر.</summary>
    public static bool InsertSignatureAtRawRangePublic(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        int rawRangeStart,
        int rawRangeEnd,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        string approverFullName,
        string? positionTitle)
        => TryInsertImageAtRawRange(
            mainPart, paragraph, rawRangeStart, rawRangeEnd,
            imageBytes, fileExtension, widthPx, imageName,
            new SignatureCaption(approverFullName, positionTitle));

    private sealed record SignatureCaption(string FullName, string? PositionTitle);

    private static bool TryInsertImageAtRawRange(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        int rawRangeStart,
        int rawRangeEnd,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        SignatureCaption? caption)
    {
        if (rawRangeEnd <= rawRangeStart)
            return false;

        var fullText = PlaceholderParagraphHelper.GetParagraphText(paragraph);
        if (string.IsNullOrEmpty(fullText))
            return false;

        rawRangeStart = Math.Clamp(rawRangeStart, 0, fullText.Length);
        rawRangeEnd = Math.Clamp(rawRangeEnd, rawRangeStart, fullText.Length);

        var spans = BuildTextSpans(paragraph);
        if (spans.Count == 0)
            return false;

        var startRun = FindRunForRawIndex(spans, rawRangeStart);
        if (startRun is null)
            return false;

        var endIndex = rawRangeEnd > rawRangeStart ? rawRangeEnd - 1 : rawRangeStart;
        var endRun = FindRunForRawIndex(spans, endIndex) ?? startRun;

        var startRunBegin = GetRunTextStart(spans, startRun);
        var startRunEnd = GetRunTextEnd(spans, startRun);
        var sameRun = ReferenceEquals(startRun, endRun);
        var midRun = sameRun && rawRangeStart > startRunBegin && rawRangeEnd < startRunEnd;

        RemoveRawTextRange(spans, rawRangeStart, rawRangeEnd);

        bool ok;
        if (midRun)
        {
            var localBefore = fullText[startRunBegin..rawRangeStart];
            var localAfter = fullText[rawRangeEnd..startRunEnd];
            ok = ApplyInlineImageReplacement(
                paragraph, mainPart, startRun, localBefore, localAfter,
                imageBytes, fileExtension, widthPx, imageName, caption);
        }
        else if (sameRun && rawRangeStart <= startRunBegin)
        {
            var localAfter = fullText[rawRangeEnd..startRunEnd];
            ok = ApplyInlineImageReplacement(
                paragraph, mainPart, startRun, "", localAfter,
                imageBytes, fileExtension, widthPx, imageName, caption);
        }
        else if (sameRun && rawRangeEnd >= startRunEnd)
        {
            var localBefore = fullText[startRunBegin..rawRangeStart];
            ok = ApplyInlineImageReplacement(
                paragraph, mainPart, startRun, localBefore, "",
                imageBytes, fileExtension, widthPx, imageName, caption);
        }
        else
        {
            ok = InsertImageBetweenRuns(
                paragraph, mainPart, startRun, imageBytes, fileExtension, widthPx, imageName, caption);
        }

        if (ok)
            PruneEmptyRuns(paragraph);

        return ok;
    }

    /// <summary>placeholder تماماً داخل یک Run نیست، یا متن قبل در Runهای قبلی است — تصویر دقیقاً بعد از Run شروع placeholder.</summary>
    private static bool InsertImageBetweenRuns(
        Paragraph paragraph,
        MainDocumentPart mainPart,
        Run startRun,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        SignatureCaption? caption)
    {
        var blockRuns = CreateBlockRuns(mainPart, imageBytes, fileExtension, widthPx, imageName, caption);

        if (RunIsEffectivelyEmpty(startRun))
        {
            var previous = FindPreviousSiblingRun(startRun);
            startRun.Remove();
            if (previous is null)
                WordOpenXmlSignatureBlockHelper.InsertRunsAfter(paragraph, null, blockRuns);
            else
                WordOpenXmlSignatureBlockHelper.InsertRunsAfter(paragraph, previous, blockRuns);
        }
        else
        {
            WordOpenXmlSignatureBlockHelper.InsertRunsBefore(paragraph, startRun, blockRuns);
        }

        return true;
    }

    /// <summary>تصویر دقیقاً بین متن قبل و بعد placeholder (inline)، نه انتهای کل Run.</summary>
    private static bool ApplyInlineImageReplacement(
        Paragraph paragraph,
        MainDocumentPart mainPart,
        Run anchorRun,
        string before,
        string after,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        SignatureCaption? caption)
    {
        var blockRuns = CreateBlockRuns(mainPart, imageBytes, fileExtension, widthPx, imageName, caption);
        var runProps = anchorRun.RunProperties?.CloneNode(true) as RunProperties;

        if (before.Length == 0 && after.Length == 0)
        {
            anchorRun.RemoveAllChildren();
            foreach (var child in blockRuns[0].ChildElements.ToList())
                anchorRun.AppendChild(child.CloneNode(true));
            if (blockRuns.Count > 1)
                WordOpenXmlSignatureBlockHelper.InsertRunsAfter(paragraph, anchorRun, blockRuns.Skip(1).ToList());
            return true;
        }

        if (before.Length == 0)
        {
            SetRunText(anchorRun, after);
            WordOpenXmlSignatureBlockHelper.InsertRunsBefore(paragraph, anchorRun, blockRuns);
            return true;
        }

        if (after.Length == 0)
        {
            SetRunText(anchorRun, before);
            WordOpenXmlSignatureBlockHelper.InsertRunsAfter(paragraph, anchorRun, blockRuns);
            return true;
        }

        SetRunText(anchorRun, before);
        WordOpenXmlSignatureBlockHelper.InsertRunsAfter(paragraph, anchorRun, blockRuns);
        var afterRun = CreateTextRun(after, runProps);
        var lastBlock = blockRuns[^1];
        paragraph.InsertAfter(afterRun, lastBlock);
        return true;
    }

    private static List<Run> CreateBlockRuns(
        MainDocumentPart mainPart,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        SignatureCaption? caption)
    {
        if (caption is null)
        {
            return
            [
                WordOpenXmlImageHelper.CreateImageRun(
                    mainPart, imageBytes, fileExtension, widthPx, imageName),
            ];
        }

        return WordOpenXmlSignatureBlockHelper.CreateRuns(
            mainPart,
            imageBytes,
            fileExtension,
            widthPx,
            imageName,
            caption.FullName,
            caption.PositionTitle);
    }

    private static Run CreateTextRun(string text, RunProperties? runProps)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (runProps is not null)
            run.RunProperties = (RunProperties)runProps.CloneNode(true);
        return run;
    }

    private static bool RunIsEffectivelyEmpty(Run run)
        => string.IsNullOrEmpty(run.InnerText?.Replace("\u00A0", "").Trim());

    private static int GetRunTextStart(List<TextSpan> spans, Run run)
    {
        foreach (var span in spans)
        {
            if (span.Node.Ancestors<Run>().FirstOrDefault() == run)
                return span.Start;
        }

        return 0;
    }

    private static int GetRunTextEnd(List<TextSpan> spans, Run run)
    {
        var end = 0;
        foreach (var span in spans)
        {
            if (span.Node.Ancestors<Run>().FirstOrDefault() == run)
                end = span.End;
        }

        return end;
    }

    private static Run? FindPreviousSiblingRun(Run run)
    {
        var prev = run.PreviousSibling();
        while (prev is not null)
        {
            if (prev is Run r && !RunIsEffectivelyEmpty(r))
                return r;
            prev = prev.PreviousSibling();
        }

        return null;
    }

    private static void PruneEmptyRuns(Paragraph paragraph)
    {
        foreach (var run in paragraph.Elements<Run>().ToList())
        {
            if (RunIsEffectivelyEmpty(run) && !run.Descendants<Drawing>().Any())
                run.Remove();
        }
    }

    private static void SetRunText(Run run, string text)
    {
        var texts = run.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return;
        }

        texts[0].Text = text;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }

    private static List<TextSpan> BuildTextSpans(Paragraph paragraph)
    {
        var spans = new List<TextSpan>();
        var pos = 0;
        foreach (var text in paragraph.Descendants<Text>())
        {
            var content = text.Text ?? "";
            spans.Add(new TextSpan { Node = text, Start = pos, End = pos + content.Length });
            pos += content.Length;
        }

        return spans;
    }

    private static Run? FindRunForRawIndex(List<TextSpan> spans, int rawIndex)
    {
        foreach (var span in spans)
        {
            if (rawIndex >= span.Start && rawIndex < span.End)
                return span.Node.Ancestors<Run>().FirstOrDefault();
        }

        var last = spans.LastOrDefault();
        if (last is not null && rawIndex == last.End)
            return last.Node.Ancestors<Run>().FirstOrDefault();

        return spans.FirstOrDefault()?.Node.Ancestors<Run>().FirstOrDefault();
    }

    private static void RemoveRawTextRange(List<TextSpan> spans, int rawRangeStart, int rawRangeEnd)
    {
        foreach (var span in spans)
        {
            if (span.End <= rawRangeStart || span.Start >= rawRangeEnd)
                continue;

            var text = span.Node.Text ?? "";
            var localStart = Math.Max(0, rawRangeStart - span.Start);
            var localEnd = Math.Min(text.Length, rawRangeEnd - span.Start);
            if (localStart >= localEnd)
                continue;

            var sb = new StringBuilder(text);
            sb.Remove(localStart, localEnd - localStart);
            span.Node.Text = sb.ToString();
            span.Node.Space = SpaceProcessingModeValues.Preserve;
        }
    }
}
