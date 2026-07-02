namespace PorslineClone.Domain.Entities;

public enum FormDispatchGroupSendJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>کار پس‌زمینه — ارسال تک‌به‌تک پیامک لینک فرم به اعضای گروه.</summary>
public class FormDispatchGroupSendJob
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Guid GroupId { get; set; }
    public Guid? WorkflowTemplateId { get; set; }
    public bool SkipWorkflow { get; set; }
    /// <summary>فقط به اعضایی که فرم هدف را تکمیل نکرده‌اند ارسال شود (از استعلام پیامک).</summary>
    public bool OnlyIncompleteSubmissions { get; set; }
    public string? SmsMessageMode { get; set; }
    public string? CustomSmsBody { get; set; }
    public FormDispatchGroupSendJobStatus Status { get; set; } = FormDispatchGroupSendJobStatus.Queued;
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
