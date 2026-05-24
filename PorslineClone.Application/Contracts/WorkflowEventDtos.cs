namespace PorslineClone.Application.Contracts;

/// <summary>رویدادهای گردش (برگشت اصلاحیه، رد قطعی و …) — JSON در Contract.WorkflowEventsJson</summary>
public class WorkflowEventDto
{
    /// <summary>
    /// rejected_for_amendment | amendment_started | amendment_completed |
    /// reapproval_requested | approved | full_rejected
    /// </summary>
    public string Kind { get; set; } = "";
    public int StepOrder { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Comment { get; set; }
    public string? RejectionType { get; set; }
    /// <summary>شماره دور اصلاحیه (۱ = اولین برگشت)</summary>
    public int Cycle { get; set; }
    public DateTime AtUtc { get; set; }
}

public record WorkflowEventViewDto(
    string Kind,
    int StepOrder,
    Guid? ActorUserId,
    string? ActorName,
    string? Comment,
    string? RejectionType,
    int Cycle,
    DateTime AtUtc);
