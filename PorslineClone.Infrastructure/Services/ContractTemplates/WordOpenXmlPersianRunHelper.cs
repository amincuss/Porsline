using System.Text;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

/// <summary>Runهای Word با فونت و جهت مناسب فارسی (UTF-16، بدون به‌هم‌ریختگی نمایش).</summary>
internal static class WordOpenXmlPersianRunHelper
{
    /// <summary>فونت پیش‌فرض فارسی در Windows/Word — Tahoma از Complex Script پشتیبانی می‌کند.</summary>
    public const string DefaultPersianFont = "Tahoma";

    public static Run CreateLineBreakRun() => new(new Break());

    /// <summary>خط بعد از تصویر inline — متن زیر امضا می‌رود نه کنار آن.</summary>
    public static Break CreateTextWrappingBreak() => new() { Type = BreakValues.TextWrapping };

    public static Run CreateTextRun(string text, bool bold = false, int halfPointSize = 18)
    {
        var normalized = NormalizePersianText(text);
        if (string.IsNullOrEmpty(normalized))
            return new Run();

        var props = new RunProperties(
            new RunFonts
            {
                Ascii = DefaultPersianFont,
                HighAnsi = DefaultPersianFont,
                ComplexScript = DefaultPersianFont,
                EastAsia = DefaultPersianFont,
            },
            new FontSize { Val = halfPointSize.ToString() },
            new FontSizeComplexScript { Val = halfPointSize.ToString() },
            new Languages { Bidi = "fa-IR", EastAsia = "fa-IR" },
            new RightToLeftText());

        if (bold)
        {
            props.AppendChild(new Bold());
            props.AppendChild(new BoldComplexScript());
        }

        var run = new Run();
        run.AppendChild(props);
        run.AppendChild(new Text(normalized) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
        return run;
    }

    public static string NormalizePersianText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var s = value.Trim().Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '\u064A' => '\u06CC', // ي arabic -> ی persian
                '\u0643' => '\u06A9', // ك arabic -> ک persian
                '\u200C' => ' ', // ZWNJ -> space for Word compatibility in captions
                _ => ch,
            });
        }

        return sb.ToString().Trim();
    }
}
