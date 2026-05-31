using System.Text.Json;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentWorkflowRunHistoryEntry
{
    public int Cycle { get; set; }
    public string Status { get; set; } = "";
    public DateTime EndedAtUtc { get; set; }
    public string? WorkflowName { get; set; }
    public string? StepsJson { get; set; }
}

public static class DocumentWorkflowRunHistoryHelper
{
    public static void SnapshotCurrentRun(Document document)
    {
        if (document.WorkflowStartedAtUtc is null)
            return;

        var cycle = Math.Max(1, document.WorkflowRunCycle);
        var list = Deserialize(document.WorkflowRunsHistoryJson);
        list.Add(new DocumentWorkflowRunHistoryEntry
        {
            Cycle = cycle,
            Status = document.WorkflowStatus.ToString(),
            EndedAtUtc = DateTime.UtcNow,
            WorkflowName = document.WorkflowName,
            StepsJson = document.StepsJson,
        });
        document.WorkflowRunsHistoryJson = JsonSerializer.Serialize(list);
    }

    public static List<DocumentWorkflowRunHistoryEntry> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<DocumentWorkflowRunHistoryEntry>>(json) ?? [];
}
