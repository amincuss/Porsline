using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class WorkflowEventHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static List<WorkflowEventDto> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<WorkflowEventDto>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<WorkflowEventDto> events)
        => JsonSerializer.Serialize(events, JsonOpts);

    public static void Append(Contract contract, WorkflowEventDto evt)
    {
        var list = Deserialize(contract.WorkflowEventsJson);
        list.Add(evt);
        contract.WorkflowEventsJson = Serialize(list);
    }

    public static int GetNextAmendmentCycle(Contract contract)
    {
        var events = Deserialize(contract.WorkflowEventsJson);
        var max = events.Where(e => e.Kind is "rejected_for_amendment" or "amendment_started")
            .Select(e => e.Cycle)
            .DefaultIfEmpty(0)
            .Max();
        return max + 1;
    }

    public static IReadOnlyList<WorkflowEventViewDto> ToViews(string? json)
        => Deserialize(json)
            .OrderBy(e => e.AtUtc)
            .Select(e => new WorkflowEventViewDto(
                e.Kind,
                e.StepOrder,
                e.ActorUserId,
                e.ActorName,
                e.Comment,
                e.RejectionType,
                e.Cycle,
                e.AtUtc))
            .ToList();

    public static string KindLabel(string kind, string? rejectionType = null) => kind switch
    {
        "rejected_for_amendment" => rejectionType == "full"
            ? "رد — اصلاحیه"
            : "رد برای اصلاحیه",
        "amendment_started" => "شروع اصلاحیه",
        "amendment_completed" => "اصلاحیه انجام شد",
        "reapproval_requested" => "بازگشت برای تأیید مجدد",
        "approved" => "تأیید",
        "full_rejected" => "رد کامل قطعی",
        _ => kind
    };
}
