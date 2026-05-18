using System.Text.Json;

namespace PorslineClone.Application.ContractTemplates;

public static class ContractTemplateFieldValuesParser
{
    public static Dictionary<string, string> Parse(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return dict;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Object => prop.Value.GetRawText(),
                JsonValueKind.Null => "",
                _ => prop.Value.GetRawText()
            };
        }

        return dict;
    }
}
