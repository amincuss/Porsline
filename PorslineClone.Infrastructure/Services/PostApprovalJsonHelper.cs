using System.Text.Json;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Infrastructure.Services;

public static class PostApprovalJsonHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static List<Guid> ParseUserIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string SerializeUserIds(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.Distinct().ToList());

    public static ContractPostApprovalStateDto? DeserializeState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ContractPostApprovalStateDto>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static string SerializeState(ContractPostApprovalStateDto state) =>
        JsonSerializer.Serialize(state);

    public static string StatusLabel(string status) => status switch
    {
        "in_progress" => "در حال انجام",
        "completed" => "اتمام کار",
        _ => "در انتظار اقدام",
    };
}
