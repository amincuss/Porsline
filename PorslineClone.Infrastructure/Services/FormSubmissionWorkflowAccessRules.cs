using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormSubmissionWorkflowAccessRules
{
    public static bool HasAssignedWorkflow(FormSubmission submission) =>
        submission.WorkflowTemplateId is not null
        || (!string.IsNullOrWhiteSpace(submission.StepsJson) && submission.StepsJson.Trim() != "[]");

    public static bool HasWorkflowActivity(FormSubmission submission)
    {
        if (submission.WorkflowStartedAtUtc is not null) return true;
        var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);
        return steps.Any(s =>
            string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Status, "rejected", StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanRestartWorkflowAfterReject(FormSubmission submission) =>
        submission.Status == FormSubmissionStatus.Rejected
        && submission.IsArchived
        && submission.WorkflowStartedAtUtc is not null
        && !FormWorkflowRejectionHelper.HasActiveRejectionFlow(submission);

    public static bool CanAssignWorkflow(FormSubmission submission)
    {
        if (submission.Status == FormSubmissionStatus.InProgress)
            return false;
        if (FormWorkflowRejectionHelper.IsAwaitingSender(submission))
            return false;
        if (CanRestartWorkflowAfterReject(submission))
            return true;
        if (HasAssignedWorkflow(submission) || HasWorkflowActivity(submission))
            return false;
        if (submission.Status == FormSubmissionStatus.Rejected)
            return false;
        return submission.Status is FormSubmissionStatus.Submitted or FormSubmissionStatus.Approved;
    }

    public static bool CanStartWorkflow(FormSubmission submission) =>
        submission.Status == FormSubmissionStatus.Pending
        && submission.WorkflowTemplateId is not null
        && submission.WorkflowStartedAtUtc is null
        && !string.IsNullOrWhiteSpace(submission.StepsJson)
        && (submission.WorkflowScheduledStartAtUtc is null || submission.WorkflowScheduledStartAtUtc <= DateTime.UtcNow);
}
