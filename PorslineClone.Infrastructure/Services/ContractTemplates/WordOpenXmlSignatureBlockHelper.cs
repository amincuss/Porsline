using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

/// <summary>بلوک امضا: تصویر + سمت + نام و نام‌خانوادگی تأییدکننده (زیر تصویر).</summary>
internal static class WordOpenXmlSignatureBlockHelper
{
    public static List<Run> CreateRuns(
        MainDocumentPart mainPart,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName,
        string approverFullName,
        string? positionTitle)
    {
        var name = WordOpenXmlPersianRunHelper.NormalizePersianText(approverFullName);
        var position = WordOpenXmlPersianRunHelper.NormalizePersianText(positionTitle);
        var hasCaption = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(position);

        var imageRun = WordOpenXmlImageHelper.CreateImageRun(
            mainPart, imageBytes, fileExtension, widthPx, imageName);

        if (!hasCaption)
            return [imageRun];

        // شکست بعد از drawing باید در همان Run باشد تا Word متن را زیر تصویر بگذارد.
        imageRun.AppendChild(WordOpenXmlPersianRunHelper.CreateTextWrappingBreak());

        var runs = new List<Run> { imageRun };

        if (!string.IsNullOrEmpty(position))
        {
            var positionRun = WordOpenXmlPersianRunHelper.CreateTextRun(position, bold: false, halfPointSize: 16);
            if (!string.IsNullOrEmpty(name))
                positionRun.AppendChild(WordOpenXmlPersianRunHelper.CreateTextWrappingBreak());
            runs.Add(positionRun);
        }

        if (!string.IsNullOrEmpty(name))
            runs.Add(WordOpenXmlPersianRunHelper.CreateTextRun(name, bold: true, halfPointSize: 18));

        return runs;
    }

    public static void InsertRunsAfter(Paragraph paragraph, OpenXmlElement? insertAfter, IReadOnlyList<Run> runs)
    {
        var cursor = insertAfter;
        foreach (var run in runs)
        {
            if (cursor is null)
                paragraph.InsertAt(run, 0);
            else
                paragraph.InsertAfter(run, cursor);
            cursor = run;
        }
    }

    public static void InsertRunsBefore(Paragraph paragraph, OpenXmlElement insertBefore, IReadOnlyList<Run> runs)
    {
        for (var i = runs.Count - 1; i >= 0; i--)
            paragraph.InsertBefore(runs[i], insertBefore);
    }
}
