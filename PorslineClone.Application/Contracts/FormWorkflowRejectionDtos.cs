namespace PorslineClone.Application.Contracts;

/// <summary>وضعیت پس از رد قطعی — تا اقدام ارسال‌کننده لینک (درخواست مجدد یا اتمام گردش)</summary>
public class FormWorkflowRejectionStateDto
{
    /// <summary>awaiting_sender | awaiting_reapprover</summary>
    public string Phase { get; set; } = "awaiting_sender";
    public int RejectedAtStepOrder { get; set; }
    public Guid RejectedByUserId { get; set; }
    public string? RejectedByUserName { get; set; }
    public string? RejectionComment { get; set; }
    public DateTime RejectedAtUtc { get; set; }
}

public record FormWorkflowRejectionViewDto(
    string Phase,
    int RejectedAtStepOrder,
    Guid RejectedByUserId,
    string? RejectedByUserName,
    string? RejectionComment,
    DateTime RejectedAtUtc,
    bool CanRequestReapproval,
    bool CanEndWorkflow);
