namespace PorslineClone.Application.Contracts;

public record WorkflowStepDto(string Id, int Order, Guid UserId, string? Note, string OnReject = "stop");

public record SaveWorkflowTemplateRequest(string Name, List<WorkflowStepDto> Steps);

public record ContractWorkflowTemplateListItemDto(
    Guid Id,
    string Name,
    int StepCount,
    bool IsActive,
    DateTime CreatedAtUtc);

public record ContractWorkflowTemplateDetailDto(
    Guid Id,
    string Name,
    bool IsActive,
    List<WorkflowStepDto> Steps);

public class ApprovalStepDto
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? UserEmail { get; set; }
    public string Status { get; set; } = "waiting";
    public string? Comment { get; set; }
    public DateTime? ActionAt { get; set; }
    public string OnReject { get; set; } = "stop";
    public string? Note { get; set; }
}
