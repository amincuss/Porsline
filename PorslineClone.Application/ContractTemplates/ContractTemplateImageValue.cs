using System.Text.Json;

namespace PorslineClone.Application.ContractTemplates;

public sealed record ContractTemplateImagePayload(string DataUrl, int WidthPx);

/// <summary>مقدار فیلد تصویر در JSON: {"dataUrl":"data:image/...","widthPx":160}</summary>
public static class ContractTemplateImageValue
{
    public const int MinWidthPx = 48;
    public const int MaxWidthPx = 480;
    public const int DefaultWidthPx = 160;

    public static bool TryParse(string? value, out ContractTemplateImagePayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = UnwrapJsonString(value.Trim());
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            payload = new ContractTemplateImagePayload(value, DefaultWidthPx);
            return true;
        }

        if (!value.StartsWith('{'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("dataUrl", out var dataUrlEl))
                return false;

            var dataUrl = dataUrlEl.GetString();
            if (string.IsNullOrWhiteSpace(dataUrl))
                return false;

            var widthPx = DefaultWidthPx;
            if (root.TryGetProperty("widthPx", out var widthEl) && widthEl.TryGetInt32(out var w))
                widthPx = ClampWidth(w);

            payload = new ContractTemplateImagePayload(dataUrl.Trim(), widthPx);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool HasImageContent(string? value)
        => TryParse(value, out var p) && !string.IsNullOrWhiteSpace(p?.DataUrl);

    public static int ClampWidth(int widthPx)
        => Math.Clamp(widthPx, MinWidthPx, MaxWidthPx);

    public static (byte[] Bytes, string Extension) DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
            throw new InvalidOperationException("فرمت تصویر نامعتبر است");

        var header = dataUrl[..comma];
        var base64 = dataUrl[(comma + 1)..];
        var bytes = Convert.FromBase64String(base64);

        var ext = ".png";
        if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            ext = ".jpg";
        else if (header.Contains("gif", StringComparison.OrdinalIgnoreCase))
            ext = ".gif";
        else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
            ext = ".webp";

        return (bytes, ext);
    }

    private static string UnwrapJsonString(string v)
    {
        if (v.StartsWith('{') || v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return v;

        if (v.Length >= 2 && v.StartsWith('"') && v.EndsWith('"'))
        {
            try
            {
                var inner = JsonSerializer.Deserialize<string>(v);
                if (!string.IsNullOrWhiteSpace(inner))
                    return inner.Trim();
            }
            catch (JsonException)
            {
                // ignore
            }
        }

        return v;
    }
}
