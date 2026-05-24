using System.Text.Json;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Infrastructure.Services;

public static class ContractAmendmentHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static ContractAmendmentStateDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ContractAmendmentStateDto>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(ContractAmendmentStateDto state)
        => JsonSerializer.Serialize(state, JsonOpts);

    public static bool IsActive(ContractAmendmentStateDto? state)
        => state is not null
           && !string.IsNullOrWhiteSpace(state.Phase)
           && state.Phase is "creator_amendment" or "first_approver_amendment"
           && state.AmendmentStatus != "done";

    public static bool CanUserActOnAmendment(
        ContractAmendmentStateDto state,
        Guid userId,
        Guid contractCreatedByUserId = default)
    {
        if (state.AssigneeUserId == userId) return true;
        if (contractCreatedByUserId == Guid.Empty || contractCreatedByUserId != userId)
            return false;
        if (state.Phase == "creator_amendment") return true;
        // ایجادکننده پس از آپلود نسخه اصلاح‌شده می‌تواند «ارسال به گردش» بزند (حتی در فاز تأییدکننده اول)
        return state.AmendedVersionNumber is not null;
    }

    public static ContractAmendmentViewDto? ToView(
        ContractAmendmentStateDto? state,
        Guid currentUserId,
        Guid contractCreatedByUserId = default)
    {
        if (!IsActive(state) || state is null) return null;
        var isCreatorPhase = state.Phase == "creator_amendment";
        var canAct = CanUserActOnAmendment(state, currentUserId, contractCreatedByUserId);
        var isCreator = contractCreatedByUserId != Guid.Empty && contractCreatedByUserId == currentUserId;
        return new ContractAmendmentViewDto(
            state.Phase,
            state.RejectionType,
            state.AmendmentStatus,
            state.AmendmentNote,
            state.RejectedAtStepOrder,
            state.RejectedByUserId,
            state.AssigneeUserId,
            canAct,
            isCreatorPhase,
            state.AmendedVersionNumber,
            isCreator && state.AmendmentStatus != "done",
            state.AmendedFileUploadedAtUtc);
    }

    public static string RejectionTypeLabel(string? type)
        => ContractWorkflowRejectionTypes.Label(type);

    public static string AmendmentStatusLabel(string? status) => status switch
    {
        "in_progress" => "در حال انجام",
        "done" => "انجام شد",
        _ => "در انتظار"
    };
}
