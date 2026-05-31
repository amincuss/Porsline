namespace PorslineClone.Domain.Entities;

/// <summary>سمت سازمانی کاربر (قابل مدیریت از تنظیمات کاربران)</summary>
public class UserPosition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
