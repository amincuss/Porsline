namespace PorslineClone.Domain.Entities;

/// <summary>قالب گروه فیلد — طراحی در «فیلد ساز» و استفاده در فرم‌ساز با درگ‌اند‌دراپ.</summary>
public class FormFieldGroupTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>آرایهٔ فیلدها همان ساختار PUT /forms/{id}/fields</summary>
    public string FieldsJson { get; set; } = "[]";
    /// <summary>تعداد فیلدهای قابل نمایش (بدون هدر مرحله ویزارد) — برای لیست سریع فیلد ساز</summary>
    public int FieldCount { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
