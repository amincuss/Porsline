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
