namespace PorslineClone.Infrastructure.Services;

public static class SmsLogMessages
{
    public static string HttpError(int? statusCode) => statusCode switch
    {
        401 or 403 => "دسترسی به سرویس پیامک مجاز نیست. با پشتیبانی تماس بگیرید.",
        404 => "آدرس سرویس پیامک یافت نشد. تنظیمات درگاه را بررسی کنید.",
        408 or 504 => "زمان پاسخ‌گویی سرویس پیامک به پایان رسید. دوباره تلاش کنید.",
        429 => "تعداد درخواست‌های پیامک بیش از حد مجاز است. کمی صبر کنید.",
        >= 500 => "سرویس پیامک موقتاً در دسترس نیست. بعداً دوباره تلاش کنید.",
        _ => "ارسال پیامک از سمت درگاه ناموفق بود.",
    };

    public static string GatewayRejected() =>
        "درگاه پیامک ارسال را تأیید نکرد. متن یا شماره گیرنده را بررسی کنید.";

    public static string ConnectionFailed() =>
        "اتصال به سرویس پیامک برقرار نشد. اتصال اینترنت یا تنظیمات درگاه را بررسی کنید.";

    public static string Unexpected() =>
        "خطای غیرمنتظره هنگام ارسال پیامک رخ داد.";
}
