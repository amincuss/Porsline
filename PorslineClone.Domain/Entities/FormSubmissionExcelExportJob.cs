namespace PorslineClone.Domain.Entities;

public enum FormSubmissionExcelExportStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>کار پس‌زمینه Hangfire — خروجی Excel پاسخ‌های فرم یک گروه.</summary>
public class FormSubmissionExcelExportJob
{
    public Guid Id { get; set; }
    public Guid? GroupId { get; set; }
    public bool UngroupedOnly { get; set; }
    public Guid FormId { get; set; }
    /// <summary>JSON array of field keys (form labels or meta:* keys).</summary>
    public string SelectedFieldsJson { get; set; } = "[]";
    public FormSubmissionExcelExportStatus Status { get; set; } = FormSubmissionExcelExportStatus.Queued;
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
