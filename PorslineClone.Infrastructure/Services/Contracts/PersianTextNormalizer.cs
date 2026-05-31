using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Options;

namespace PorslineClone.Infrastructure.Services.Contracts;

/// <summary>نرمال‌سازی متن فارسی برای ذخیره و جستجو.</summary>
public sealed partial class PersianTextNormalizer(IOptions<PersianTextNormalizerOptions> options) : IPersianTextNormalizer
{
    private static readonly Regex MultiSpace = MultiSpaceRegex();
    private static readonly Regex MultiNewline = MultiNewlineRegex();
    private readonly PersianTextNormalizerOptions _opts = options.Value;

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.Normalize(NormalizationForm.FormC))
        {
            if (IsArabicDiacritic(ch))
                continue;

            sb.Append(ch switch
            {
                '\u064A' => '\u06CC', // ي -> ی
                '\u0643' => '\u06A9', // ك -> ک
                '\u200C' or '\u200D' => ' ', // ZWNJ/ZWJ
                '\u200E' or '\u200F' or '\u202A' or '\u202B' or '\u202C' => ' ',
                '\0' => ' ',
                _ when char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' => ' ',
                _ => ch,
            });
        }

        var text = sb.ToString();
        if (_opts.NormalizeDigitsToLatin)
            text = ArabicPersianDigitsToLatin(text);

        text = MultiNewline.Replace(text, "\n");
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    private static bool IsArabicDiacritic(char ch) =>
        ch is >= '\u064B' and <= '\u065F' or '\u0670' or '\u0640';

    private static string ArabicPersianDigitsToLatin(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                _ => chars[i],
            };
        }
        return new string(chars);
    }

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiNewlineRegex();
}
