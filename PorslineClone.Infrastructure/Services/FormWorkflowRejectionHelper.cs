using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormWorkflowRejectionHelper
{
    public static FormWorkflowRejectionStateDto? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<FormWorkflowRejectionStateDto>(json);

    public static string Serialize(FormWorkflowRejectionStateDto state) =>
        JsonSerializer.Serialize(state);

    public static bool IsAwaitingSender(FormSubmission submission) =>
        submission.Status == FormSubmissionStatus.Rejected
        && !submission.IsArchived
        && Deserialize(submission.WorkflowRejectionJson) is { Phase: "awaiting_sender" };

    public static bool IsAwaitingReapprover(FormSubmission submission) =>
        submission.Status == FormSubmissionStatus.InProgress
        && Deserialize(submission.WorkflowRejectionJson) is { Phase: "awaiting_reapprover" };

    public static bool HasActiveRejectionFlow(FormSubmission submission) =>
        Deserialize(submission.WorkflowRejectionJson) is not null && !submission.IsArchived;

    public static FormWorkflowRejectionViewDto? BuildView(FormSubmission submission, bool isDispatchSender)
    {
        var state = Deserialize(submission.WorkflowRejectionJson);
        if (state is null || submission.IsArchived) return null;

        var awaitingSender = state.Phase == "awaiting_sender";

        return new FormWorkflowRejectionViewDto(
            state.Phase,
            state.RejectedAtStepOrder,
            state.RejectedByUserId,
            state.RejectedByUserName,
            state.RejectionComment,
            state.RejectedAtUtc,
            CanRequestReapproval: awaitingSender && isDispatchSender,
            CanEndWorkflow: awaitingSender && isDispatchSender);
    }
}
