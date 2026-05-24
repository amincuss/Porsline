namespace PorslineClone.Application.Contracts;

public static class PostApprovalDirections
{
    public static readonly (string Key, string Label)[] Items =
    [
        ("follow_up", "پیگیری و اجرای تصمیم"),
        ("financial", "اقدام مالی و تسویه"),
        ("legal", "اقدام حقوقی"),
        ("archive", "بایگانی و اختتام پرونده"),
        ("notify_party", "اطلاع‌رسانی به طرف قرارداد"),
    ];

    public static string? LabelFor(string? key) =>
        Items.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)).Label;
}

public record PostApprovalTemplateConfigDto(
    string? ActionDirectionKey,
    string? ActionDirectionLabel,
    List<Guid> ActionAssigneeUserIds);

public record ContractPostApprovalStateDto(
    string ActionDirectionKey,
    string ActionDirectionLabel,
    List<Guid> AssigneeUserIds,
    string Status,
    string? Note,
    Guid? UpdatedByUserId,
    string? UpdatedByUserName,
    DateTime? UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public record ContractActionListItemDto(
    Guid ContractId,
    string ContractNumber,
    string Title,
    string SubjectPersonName,
    string ActionDirectionLabel,
    string Status,
    string StatusLabel,
    DateTime? UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime ApprovedAtUtc,
    string? WorkflowName);

public record ContractActionDetailDto(
    Guid ContractId,
    string ContractNumber,
    string Title,
    string SubjectPersonName,
    string PartyName,
    string FirstName,
    string LastName,
    string NationalId,
    string Phone,
    DateTime DateFromUtc,
    DateTime DateToUtc,
    string? ContractTypeName,
    string? WorkflowName,
    string ActionDirectionLabel,
    string Status,
    string StatusLabel,
    string? Note,
    DateTime? UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    string? UpdatedByUserName,
    IReadOnlyList<string> AssigneeNames,
    Dictionary<string, string>? TemplateFieldValues,
    List<ApprovalStepDto> Steps,
    bool CanUpdate);

public record UpdateContractActionStatusRequest(string Status, string? Note);

public record ContractActionPhaseAssigneeDto(Guid UserId, string UserName);

public record ContractActionPhaseViewDto(
    string ActionDirectionLabel,
    string? Status,
    string? StatusLabel,
    IReadOnlyList<ContractActionPhaseAssigneeDto> Assignees,
    string? UpdatedByUserName,
    DateTime? UpdatedAtUtc);
