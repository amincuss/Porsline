using System.Text;
using System.Text.RegularExpressions;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>نرمال‌سازی متن فارسی/عربی برای ذخیره و جستجو.</summary>
public sealed partial class FarsiTextNormalizer : IFarsiTextNormalizer
{
    private static readonly Regex MultiSpace = MultiSpaceRegex();

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(ch switch
            {
                '\u064A' => '\u06CC', // ي -> ی
                '\u0643' => '\u06A9', // ك -> ک
                '\u200C' => ' ',      // ZWNJ -> space (optional; helps FTS tokenization)
                '\u200D' => ' ',
                '\u200E' or '\u200F' or '\u202A' or '\u202B' or '\u202C' => ' ',
                _ when char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' => ' ',
                _ => ch,
            });
        }

        var text = sb.ToString();
        text = ArabicIndicToLatinDigits(text);
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    private static string ArabicIndicToLatinDigits(string text)
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
}
