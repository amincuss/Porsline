namespace PorslineClone.Infrastructure.Services;

/// <summary>نرمال‌سازی موبایل برای پیامک — هماهنگ با PublicFormsController و صفحه ثبت فرم.</summary>
public static class FormSubmissionMobileHelper
{
    public static string NormalizeMobile(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var mapped = input.Trim()
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        return new string(mapped.Where(char.IsDigit).ToArray());
    }

    /// <summary>موبایل پاسخگو/ثبت‌نام‌کننده — اولویت با شماره لینک ارسال (همان چیزی که در UI نمایش داده می‌شود).</summary>
    public static string ResolveRegistrantMobile(
        string? linkResponderMobile,
        string? responderEntityMobile,
        string? submissionSubmitterMobile)
    {
        foreach (var raw in new[] { linkResponderMobile, responderEntityMobile, submissionSubmitterMobile })
        {
            var n = NormalizeMobile(raw);
            if (n.Length >= 10) return n;
        }
        return "";
    }

}
