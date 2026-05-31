using System.Text.Json;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormFieldGroupJsonHelper
{
    public static int CountNonHeaderFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
            var count = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var ft = ReadFieldType(el);
                if (ft is null or (int)FieldType.WizardStepHeader) continue;
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static int? ReadFieldType(JsonElement el)
    {
        if (el.TryGetProperty("fieldType", out var ft)) return ft.GetInt32();
        if (el.TryGetProperty("FieldType", out ft)) return ft.GetInt32();
        return null;
    }
}
