using System.Text.Json;
using System.Text.Json.Serialization;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Infrastructure.Services;

/// <summary>سریال‌سازی یکسان مراحل گردش — camelCase و نرمال‌سازی وضعیت/ترتیب.</summary>
public static class WorkflowStepJsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<ApprovalStepDto> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var steps = JsonSerializer.Deserialize<List<ApprovalStepDto>>(json, Options) ?? [];
        return Normalize(steps);
    }

    public static string Serialize(List<ApprovalStepDto> steps) =>
        JsonSerializer.Serialize(Normalize(steps), Options);

    /// <summary>ترتیب ۱..n، وضعیت lowercase، حذف pending تکراری.</summary>
    public static List<ApprovalStepDto> Normalize(List<ApprovalStepDto> steps)
    {
        if (steps.Count == 0) return steps;

        var ordered = steps.OrderBy(s => s.Order <= 0 ? int.MaxValue : s.Order).ThenBy(s => s.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
            ordered[i].Status = NormalizeStatus(ordered[i].Status);
        }

        var pending = ordered.Where(s => s.Status == "pending").ToList();
        if (pending.Count > 1)
        {
            var keep = pending.OrderBy(s => s.Order).First();
            foreach (var s in ordered)
            {
                if (s != keep && s.Status == "pending")
                    s.Status = "waiting";
            }
        }

        return ordered;
    }

    public static string NormalizeStatus(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "approved" => "approved",
            "rejected" => "rejected",
            "skipped" => "skipped",
            "pending" => "pending",
            _ => "waiting",
        };
    }

    public static ApprovalStepDto? FindCurrentPending(List<ApprovalStepDto> steps, int currentStepOrder)
    {
        var byPointer = steps.FirstOrDefault(s =>
            s.Order == currentStepOrder
            && string.Equals(s.Status, "pending", StringComparison.OrdinalIgnoreCase));
        if (byPointer is not null) return byPointer;

        return steps
            .Where(s => string.Equals(s.Status, "pending", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Order)
            .FirstOrDefault();
    }

    public static ApprovalStepDto? FindNextStep(List<ApprovalStepDto> steps, ApprovalStepDto current)
    {
        var next = steps
            .Where(s => s.Order > current.Order)
            .OrderBy(s => s.Order)
            .FirstOrDefault();
        if (next is not null) return next;

        return steps
            .Where(s => string.Equals(s.Status, "waiting", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Order)
            .FirstOrDefault();
    }

    public static void SetSinglePending(List<ApprovalStepDto> steps, ApprovalStepDto pendingStep)
    {
        foreach (var s in steps)
        {
            if (s.Order == pendingStep.Order)
            {
                s.Status = "pending";
                continue;
            }

            var st = NormalizeStatus(s.Status);
            if (st is "approved" or "rejected" or "skipped")
                s.Status = st;
            else
                s.Status = "waiting";
        }
    }
}
