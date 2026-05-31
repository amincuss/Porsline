using Microsoft.Extensions.Options;
using PorslineClone.Infrastructure.Options;
using PorslineClone.Infrastructure.Services.Contracts;

namespace PorslineClone.Tests;

public class PersianTextNormalizerTests
{
    private readonly PersianTextNormalizer _normalizer = new(
        Options.Create(new PersianTextNormalizerOptions { NormalizeDigitsToLatin = true }));

    [Fact]
    public void Normalize_ArabicYehKaf_ToPersian()
    {
        var result = _normalizer.Normalize("علي كرمي");
        Assert.Contains('\u06CC', result);
        Assert.Contains('\u06A9', result);
        Assert.DoesNotContain('\u064A', result);
        Assert.DoesNotContain('\u0643', result);
    }

    [Fact]
    public void Normalize_RemovesDiacritics()
    {
        var result = _normalizer.Normalize("كِتاب");
        Assert.DoesNotContain('\u0650', result);
    }

    [Fact]
    public void Normalize_PersianAndArabicDigits_ToLatin()
    {
        Assert.Equal("123", _normalizer.Normalize("۱۲۳"));
        Assert.Equal("456", _normalizer.Normalize("٤٥٦"));
    }

    [Fact]
    public void Normalize_Zwnj_ToSpace()
    {
        var result = _normalizer.Normalize("می\u200Cخواهم");
        Assert.DoesNotContain('\u200C', result);
        Assert.Contains("می", result);
        Assert.Contains("خواهم", result);
    }

    [Fact]
    public void Normalize_CollapsesWhitespace()
    {
        Assert.Equal("سلام دنیا", _normalizer.Normalize("سلام   دنیا"));
    }

    [Fact]
    public void Normalize_RemovesNullAndControlChars()
    {
        Assert.Equal("abc", _normalizer.Normalize("a\u0000b\u0001c"));
    }
}
