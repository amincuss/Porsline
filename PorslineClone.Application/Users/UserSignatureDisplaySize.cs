namespace PorslineClone.Application.Users;

/// <summary>سایز نمایش امضا در Word — درجهٔ UI به عرض پیکسل تبدیل می‌شود.</summary>
public static class UserSignatureDisplaySize
{
    public static readonly int[] AllowedDegrees = [30, 45, 60, 75, 90];

    private static readonly IReadOnlyDictionary<int, int> DegreeToWidthPx = new Dictionary<int, int>
    {
        [30] = 90,
        [45] = 110,
        [60] = 140,
        [75] = 170,
        [90] = 200,
    };

    public const int DefaultDegree = 60;

    public static int NormalizeDegree(int? degree)
    {
        if (degree is null) return DefaultDegree;
        return AllowedDegrees.Contains(degree.Value) ? degree.Value : DefaultDegree;
    }

    public static int WidthPxFromDegree(int? degree) =>
        DegreeToWidthPx[NormalizeDegree(degree)];

    public static int WidthPxFromDegree(int degree) => WidthPxFromDegree((int?)degree);

    public static string LabelForDegree(int degree) => $"{NormalizeDegree(degree)}°";
}
