using System.Net.Http.Headers;

namespace PorslineClone.Api.Http;

/// <summary>
/// Kestrel rejects non-ASCII in Content-Disposition; use filename (ASCII) + filename* (UTF-8).
/// </summary>
public static class ContentDispositionHelper
{
    public static void SetInline(HttpResponse response, string fileName)
        => Set(response, "inline", fileName);

    public static void SetAttachment(HttpResponse response, string fileName)
        => Set(response, "attachment", fileName);

    private static void Set(HttpResponse response, string dispositionType, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName.Trim();
        var cd = new ContentDispositionHeaderValue(dispositionType)
        {
            FileName = ToAsciiFileName(name),
            FileNameStar = name,
        };
        response.Headers.ContentDisposition = cd.ToString();
    }

    private static string ToAsciiFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.Length > 0
            && baseName.All(c => c < 0x80 && !char.IsControl(c) && c is not '"' and not '\\'))
            return fileName;

        return string.IsNullOrEmpty(ext) ? "document" : $"document{ext}";
    }
}
