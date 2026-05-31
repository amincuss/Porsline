using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.Documents;

public static class DocumentWorkflowAccessRules
{
    public static bool HasAssignedWorkflow(Document document) =>
        document.WorkflowTemplateId is not null
        || (!string.IsNullOrWhiteSpace(document.StepsJson) && document.StepsJson.Trim() != "[]");

    public static bool HasWorkflowActivity(Document document)
    {
        if (document.WorkflowStartedAtUtc is not null) return true;
        var steps = DocumentWorkflowProcessor.DeserializeSteps(document.StepsJson);
        return steps.Any(s =>
            string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Status, "rejected", StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanRestartWorkflowAfterReject(Document document) =>
        document.WorkflowStatus == DocumentWorkflowStatus.Rejected
        && document.WorkflowStartedAtUtc is not null
        && !DocumentWorkflowRejectionHelper.HasActiveRejectionFlow(document);

    public static bool CanAssignWorkflow(Document document)
    {
        if (document.WorkflowStatus == DocumentWorkflowStatus.InProgress)
            return false;
        if (DocumentWorkflowRejectionHelper.IsAwaitingSender(document))
            return false;
        if (CanRestartWorkflowAfterReject(document))
            return true;
        if (HasAssignedWorkflow(document) || HasWorkflowActivity(document))
            return false;
        if (document.WorkflowStatus == DocumentWorkflowStatus.Rejected)
            return false;
        return document.WorkflowStatus is DocumentWorkflowStatus.None or DocumentWorkflowStatus.Approved;
    }

    public static bool CanStartWorkflow(Document document) =>
        document.WorkflowStatus == DocumentWorkflowStatus.Pending
        && document.WorkflowTemplateId is not null
        && document.WorkflowStartedAtUtc is null
        && !string.IsNullOrWhiteSpace(document.StepsJson)
        && (document.WorkflowScheduledStartAtUtc is null || document.WorkflowScheduledStartAtUtc <= DateTime.UtcNow);

    public static bool CanUnassignWorkflow(Document document) =>
        document.WorkflowStartedAtUtc is null
        && document.WorkflowStatus is not DocumentWorkflowStatus.InProgress
        && HasAssignedWorkflow(document);
}
