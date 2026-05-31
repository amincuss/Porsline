using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.Documents;

public static class DocumentWorkflowRejectionHelper
{
    public static FormWorkflowRejectionStateDto? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<FormWorkflowRejectionStateDto>(json);

    public static string Serialize(FormWorkflowRejectionStateDto state) =>
        JsonSerializer.Serialize(state);

    public static bool IsAwaitingSender(Document document) =>
        document.WorkflowStatus == DocumentWorkflowStatus.Rejected
        && Deserialize(document.WorkflowRejectionJson) is { Phase: "awaiting_sender" };

    public static bool HasActiveRejectionFlow(Document document) =>
        Deserialize(document.WorkflowRejectionJson) is not null;

    public static FormWorkflowRejectionViewDto? BuildView(Document document, bool isOwner)
    {
        var state = Deserialize(document.WorkflowRejectionJson);
        if (state is null) return null;

        var awaitingSender = state.Phase == "awaiting_sender";

        return new FormWorkflowRejectionViewDto(
            state.Phase,
            state.RejectedAtStepOrder,
            state.RejectedByUserId,
            state.RejectedByUserName,
            state.RejectionComment,
            state.RejectedAtUtc,
            CanRequestReapproval: awaitingSender && isOwner,
            CanEndWorkflow: awaitingSender && isOwner);
    }
}
