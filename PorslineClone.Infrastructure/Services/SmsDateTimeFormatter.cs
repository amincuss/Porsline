using System.Globalization;

namespace PorslineClone.Infrastructure.Services;

/// <summary>تاریخ و ساعت شمسی تهران با ارقام فارسی — برای متن پیامک.</summary>
public static class SmsDateTimeFormatter
{
    private static readonly TimeZoneInfo TehranZone = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
    private static readonly PersianCalendar Persian = new();

    public static (string Date, string Time) FormatUtcNowTehran() => FormatUtcTehran(DateTime.UtcNow);

    public static (string Date, string Time) FormatUtcTehran(DateTime utc)
    {
        var tehran = ToTehranLocal(utc);
        var date = $"{Persian.GetYear(tehran):0000}/{Persian.GetMonth(tehran):00}/{Persian.GetDayOfMonth(tehran):00}";
        var time = tehran.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (ToPersianDigits(date), ToPersianDigits(time));
    }

    public static string FormatUtcTehranDate(DateTime utc) => FormatUtcTehran(utc).Date;

    public static string FormatUtcTehranTime(DateTime utc) => FormatUtcTehran(utc).Time;

    public static string ToPersianDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
            .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
    }

    private static DateTime ToTehranLocal(DateTime utc)
    {
        var normalized = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, TehranZone);
    }
}
