using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PorslineClone.Infrastructure.Services;

public static class DocxTextExtractor
{
    public static string ExtractPlainText(string filePath)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        using var stream = File.OpenRead(filePath);
        return ExtractPlainText(stream);
    }

    public static string ExtractPlainText(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var line = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(line))
            {
                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.AppendLine();
                continue;
            }

            sb.AppendLine(line.Trim());
        }

        return sb.ToString().Trim();
    }
}
