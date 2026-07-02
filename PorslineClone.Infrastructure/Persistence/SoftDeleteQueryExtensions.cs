using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Persistence;

/// <summary>
/// کمک‌متدهای انتخاب موجودیت‌های فعال — فیلتر حذف نرم در AppDbContext به‌صورت سراسری اعمال می‌شود.
/// </summary>
public static class SoftDeleteQueryExtensions
{
    /// <summary>کاربران قابل انتخاب (حذف نرم نشده و فعال).</summary>
    public static IQueryable<AppUser> SelectableUsers(this IQueryable<AppUser> query) =>
        query.Where(x => x.IsActive);

    /// <summary>پاسخگوهای فعال — فیلتر IsDeleted در سطح DbContext نیز اعمال می‌شود.</summary>
    public static IQueryable<Responder> ActiveResponders(this IQueryable<Responder> query) => query;

    /// <summary>گروه‌های پاسخگو/کاربر فعال — فیلتر IsDeleted در سطح DbContext نیز اعمال می‌شود.</summary>
    public static IQueryable<ResponderGroup> ActiveResponderGroups(this IQueryable<ResponderGroup> query) =>
        query.Where(x => x.IsActive);

    public static IQueryable<UserGroup> ActiveUserGroups(this IQueryable<UserGroup> query) =>
        query.Where(x => x.IsActive);
}
