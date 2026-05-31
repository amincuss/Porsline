namespace PorslineClone.Domain.Entities;

public enum FormWordBatchExportStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>کار پس‌زمینه Hangfire — تبدیل گروهی به Word و ساخت ZIP.</summary>
public class FormWordBatchExportJob
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public string SubmissionIdsJson { get; set; } = "[]";
    public string? ImageOverridesJson { get; set; }
    public FormWordBatchExportStatus Status { get; set; } = FormWordBatchExportStatus.Queued;
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public string? ZipFilePath { get; set; }
    public string? ZipFileName { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
