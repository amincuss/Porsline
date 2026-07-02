namespace PorslineClone.Infrastructure.Services.SmsPatterns;

public static class SmsPatternVars
{
    public static Dictionary<string, string?> Dict(params (string Key, string? Value)[] items) =>
        items.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
}
