using System.Text.Json;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public sealed class FormWorkflowRunHistoryEntry
{
    public int Cycle { get; set; }
    public string Status { get; set; } = "";
    public DateTime EndedAtUtc { get; set; }
    public string? WorkflowName { get; set; }
    public string? StepsJson { get; set; }
}

public static class FormWorkflowRunHistoryHelper
{
    public static void SnapshotCurrentRun(FormSubmission submission)
    {
        if (submission.WorkflowStartedAtUtc is null)
            return;

        var cycle = Math.Max(1, submission.WorkflowRunCycle);
        var list = Deserialize(submission.WorkflowRunsHistoryJson);
        list.Add(new FormWorkflowRunHistoryEntry
        {
            Cycle = cycle,
            Status = submission.Status.ToString(),
            EndedAtUtc = DateTime.UtcNow,
            WorkflowName = submission.WorkflowName,
            StepsJson = submission.StepsJson,
        });
        submission.WorkflowRunsHistoryJson = JsonSerializer.Serialize(list);
    }

    public static List<FormWorkflowRunHistoryEntry> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<FormWorkflowRunHistoryEntry>>(json) ?? [];
}
