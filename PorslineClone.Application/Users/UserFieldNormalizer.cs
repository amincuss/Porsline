using System.Text;
using System.Text.RegularExpressions;

namespace PorslineClone.Application.Users;

public static class UserFieldNormalizer
{
    private static readonly Regex MobileRegex = new(@"^09\d{9}$", RegexOptions.Compiled);

    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            if (ch is >= '0' and <= '9')
                sb.Append(ch);
            else if (ch is >= '\u06F0' and <= '\u06F9')
                sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch is >= '\u0660' and <= '\u0669')
                sb.Append((char)('0' + (ch - '\u0660')));
        }
        return sb.ToString();
    }

    public static string NormalizeMobile(string? raw)
    {
        var digits = NormalizeDigits(raw);
        if (digits.Length == 0) return "";
        if (digits.Length == 10 && digits.StartsWith('9')) return "0" + digits;
        return digits.Length > 11 ? digits[^11..] : digits;
    }

    public static string NormalizeNationalCode(string? raw)
    {
        var digits = NormalizeDigits(raw);
        if (digits.Length == 0) return "";
        if (digits.Length <= 10) return digits.PadLeft(10, '0');
        return digits[^10..];
    }

    public static bool IsValidMobile(string mobile) => MobileRegex.IsMatch(mobile);

    public static bool IsValidNationalCode(string nationalCode) =>
        nationalCode.Length == 10 && nationalCode.All(char.IsDigit);
}
