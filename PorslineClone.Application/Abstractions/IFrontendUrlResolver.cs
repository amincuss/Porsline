namespace PorslineClone.Application.Abstractions;

public interface IFrontendUrlResolver
{
    /// <summary>آدرس پایه برای لینک‌های عمومی (مثل پر کردن فرم). ابتدا دیتابیس، سپس Frontend:BaseUrl.</summary>
    Task<string?> ResolvePublicBaseUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>آدرس پایه پنل ادمین (ورود، تأییدیه). ابتدا دیتابیس، سپس public، سپس config.</summary>
    Task<string?> ResolveAdminBaseUrlAsync(CancellationToken cancellationToken = default);
}
