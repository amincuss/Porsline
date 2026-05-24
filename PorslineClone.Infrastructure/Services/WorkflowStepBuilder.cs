using System.Text.Json;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Infrastructure.Services;

public static class WorkflowStepBuilder
{
    public static List<ApprovalStepDto> BuildApprovalStepsFromTemplate(string? workflowJson, bool startImmediately)
    {
        if (string.IsNullOrWhiteSpace(workflowJson)) return [];
        var workflow = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowJson) ?? [];
        return workflow
            .OrderBy(x => x.Order)
            .Select((x, i) => new ApprovalStepDto
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                UserId = x.UserId,
                Status = startImmediately && i == 0 ? "pending" : "waiting",
                OnReject = x.OnReject is "continue" ? "continue" : "stop",
                Note = x.Note,
                ApprovalDeadlineDays = Math.Max(0, x.ApprovalDeadlineDays ?? 0),
                ApprovalDeadlineHours = Math.Max(0, x.ApprovalDeadlineHours ?? 0),
            })
            .ToList();
    }

    public static List<ApprovalStepDto> BuildApprovalStepsFromInline(string? workflowJson, bool startImmediately)
    {
        if (string.IsNullOrWhiteSpace(workflowJson)) return [];
        var workflow = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowJson) ?? [];
        return workflow
            .OrderBy(x => x.Order)
            .Select((x, i) => new ApprovalStepDto
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                UserId = x.UserId,
                Status = startImmediately && i == 0 ? "pending" : "waiting",
                OnReject = x.OnReject is "continue" ? "continue" : "stop",
                Note = x.Note,
                ApprovalDeadlineDays = Math.Max(0, x.ApprovalDeadlineDays ?? 0),
                ApprovalDeadlineHours = Math.Max(0, x.ApprovalDeadlineHours ?? 0),
            })
            .ToList();
    }
}
