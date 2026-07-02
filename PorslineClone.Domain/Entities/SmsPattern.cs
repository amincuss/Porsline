namespace PorslineClone.Domain.Entities;

/// <summary>قالب متن پیامک — کلید یکتا و placeholder با {name}.</summary>
public class SmsPattern
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = "MessageSquare";
    public string IconColor { get; set; } = "#8B5CF6";
    public string Template { get; set; } = string.Empty;
    /// <summary>JSON: [{ "key": "formTitle", "label": "عنوان فرم" }]</summary>
    public string PlaceholdersJson { get; set; } = "[]";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
