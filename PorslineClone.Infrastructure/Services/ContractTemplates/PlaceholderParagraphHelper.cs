using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

internal static class PlaceholderParagraphHelper
{
    public static string GetParagraphText(Paragraph paragraph)
        => string.Concat(paragraph.Descendants<Text>().Select(t => t.Text ?? ""));

    public static string NormalizeForPlaceholderMatch(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\u00A0':
                case '\u2007':
                case '\u202F':
                    sb.Append(' ');
                    break;
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                    continue;
                case '｛':
                    sb.Append('{');
                    break;
                case '｝':
                    sb.Append('}');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>متن نرمال‌شده + نگاشت index نرمال → index خام (برای درج دقیق تصویر).</summary>
    public static (string Normalized, int[] NormIndexToRaw) BuildNormalizedWithIndexMap(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return ("", []);

        var norm = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '\u00A0':
                case '\u2007':
                case '\u202F':
                    norm.Append(' ');
                    map.Add(i);
                    break;
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                    break;
                case '｛':
                    norm.Append('{');
                    map.Add(i);
                    break;
                case '｝':
                    norm.Append('}');
                    map.Add(i);
                    break;
                default:
                    norm.Append(ch);
                    map.Add(i);
                    break;
            }
        }

        return (norm.ToString(), map.ToArray());
    }

    public static (int RawStart, int RawEnd) MapNormalizedRangeToRaw(int[] normToRaw, int normStart, int normLength)
    {
        if (normToRaw.Length == 0 || normLength <= 0)
            return (0, 0);

        var normEnd = normStart + normLength - 1;
        if (normStart < 0 || normStart >= normToRaw.Length)
            return (0, 0);

        normEnd = Math.Clamp(normEnd, 0, normToRaw.Length - 1);
        return (normToRaw[normStart], normToRaw[normEnd] + 1);
    }

    /// <summary>متن همه Text nodeها را در اولین node می‌ریزد (placeholderهای شکسته در Word).</summary>
    public static void CollapseParagraphText(Paragraph paragraph)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count <= 1)
            return;

        var combined = string.Concat(textNodes.Select(t => t.Text ?? ""));
        if (string.IsNullOrEmpty(combined))
            return;

        textNodes[0].Text = combined;
        textNodes[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < textNodes.Count; i++)
            textNodes[i].Text = "";
    }
}
