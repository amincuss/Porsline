namespace PorslineClone.Infrastructure.Options;

public class DatabaseStartupOptions
{
    public const string SectionName = "Database";

    /// <summary>اجرای EF migrations هنگام استارت — پیش‌فرض خاموش؛ دستی با dotnet ef database update</summary>
    public bool RunMigrations { get; set; } = false;

    public bool RunSeed { get; set; } = true;

    /// <summary>پچ SQL هنگام استارت — پیش‌فرض خاموش؛ در صورت نیاز دستی یا SQL اجرا کنید</summary>
    public bool ApplySchemaPatch { get; set; } = false;

    /// <summary>اگر migration خطا داد ولی پچ موفق بود، اپ بالا بیاید</summary>
    public bool ContinueOnMigrationError { get; set; } = true;
}
