namespace PorslineClone.Application.Contracts;

public record FormPostApprovalMessageDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string Text,
    DateTime AtUtc);

public record FormPostApprovalStateDto(
    IReadOnlyList<Guid> AssigneeUserIds,
    string Phase,
    IReadOnlyList<FormPostApprovalMessageDto> Messages,
    string? CompletionNote,
    DateTime? CompletedAtUtc,
    Guid? CompletedByUserId);

public record FormPostApprovalListItemDto(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string? WorkflowName,
    DateTime SubmittedAtUtc,
    DateTime? CompletedAtUtc,
    string Phase,
    string SubjectSummary);

public record PostFormPostApprovalMessageRequest(string Text);

public record CompleteFormPostApprovalRequest(string Note);
