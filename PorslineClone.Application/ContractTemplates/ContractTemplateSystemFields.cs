using PorslineClone.Domain.Entities;

namespace PorslineClone.Application.ContractTemplates;

/// <summary>فیلدهای سیستمی قالب (شماره قرارداد، امضا و …)</summary>
public static class ContractTemplateSystemFields
{
    public const string ContractNumberKey = "contract_number";

    private static readonly HashSet<string> ContractNumberKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ContractNumberKey,
        "contract_no",
        "contractnumber",
        "shomare_gharardad",
        "shomare_gardan",
    };

    private static readonly HashSet<string> ImageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "photo",
        "picture",
        "logo",
        "aks",
        "tasvir",
        "company_logo",
    };

    public static bool IsContractNumberKey(string key)
    {
        var norm = NormalizeKey(key);
        return !string.IsNullOrWhiteSpace(norm) && ContractNumberKeys.Contains(norm);
    }

    public static bool IsImageKey(string key)
    {
        var norm = NormalizeKey(key);
        return !string.IsNullOrWhiteSpace(norm) && ImageKeys.Contains(norm);
    }

    /// <summary>کلیدهای رایج placeholder تاریخ (date، date1، tarikh، …)</summary>
    public static bool IsDateKey(string key)
    {
        var norm = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(norm))
            return false;
        if (norm is "date" or "tarikh" or "birth_date" or "start_date" or "end_date")
            return true;
        if (norm.StartsWith("date", StringComparison.Ordinal) && norm.Length > 4 && norm[4..].All(char.IsDigit))
            return true;
        return norm.EndsWith("_date", StringComparison.Ordinal);
    }

    public static bool IsSystemFieldType(ContractTemplateFieldType fieldType) =>
        fieldType is ContractTemplateFieldType.Signature or ContractTemplateFieldType.ContractNumber;

    public static Dictionary<string, string> MergeContractNumber(
        IEnumerable<ContractDocumentTemplateField> versionFields,
        IReadOnlyDictionary<string, string> userValues,
        string contractNumber)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in userValues)
        {
            var norm = NormalizeKey(kv.Key);
            if (string.IsNullOrWhiteSpace(norm))
                continue;
            result[norm] = kv.Value ?? "";
        }

        if (string.IsNullOrWhiteSpace(contractNumber))
            return result;

        foreach (var f in versionFields)
        {
            if (f.FieldType != ContractTemplateFieldType.ContractNumber && !IsContractNumberKey(f.Key))
                continue;
            var key = NormalizeKey(f.Key);
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = contractNumber;
        }

        return result;
    }

    public static string NormalizeKey(string key)
        => new string((key ?? "").Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()).ToLowerInvariant();
}
