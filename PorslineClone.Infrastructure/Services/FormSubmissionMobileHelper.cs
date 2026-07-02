using PorslineClone.Application.Users;

namespace PorslineClone.Infrastructure.Services;

/// <summary>نرمال‌سازی موبایل برای پیامک — همان منطق UserFieldNormalizer (ورود/کاربران).</summary>
public static class FormSubmissionMobileHelper
{
    public static string NormalizeMobile(string? input) => UserFieldNormalizer.NormalizeMobile(input);

    public static bool IsValidMobile(string? input) =>
        UserFieldNormalizer.IsValidMobile(NormalizeMobile(input));

    /// <summary>موبایل پاسخگو/ثبت‌نام‌کننده — اولویت با شماره لینک ارسال.</summary>
    public static string ResolveRegistrantMobile(
        string? linkResponderMobile,
        string? responderEntityMobile,
        string? submissionSubmitterMobile)
    {
        foreach (var raw in new[] { linkResponderMobile, responderEntityMobile, submissionSubmitterMobile })
        {
            var n = NormalizeMobile(raw);
            if (IsValidMobile(n)) return n;
        }
        return "";
    }
}
