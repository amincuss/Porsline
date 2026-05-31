namespace PorslineClone.Infrastructure.Options;

public sealed class PersianTextNormalizerOptions
{
    public const string SectionName = "PersianTextNormalizer";

    /// <summary>تبدیل ارقام عربی/فارسی به لاتین</summary>
    public bool NormalizeDigitsToLatin { get; set; } = true;
}
