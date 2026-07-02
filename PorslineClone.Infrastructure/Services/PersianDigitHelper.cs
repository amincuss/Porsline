namespace PorslineClone.Infrastructure.Services;

public static class PersianDigitHelper
{
    private const string En = "0123456789";
    private const string Fa = "۰۱۲۳۴۵۶۷۸۹";
    private const string Ar = "٠١٢٣٤٥٦٧٨٩";

    public static string ToPersian(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var enIdx = En.IndexOf(chars[i]);
            if (enIdx >= 0) { chars[i] = Fa[enIdx]; continue; }
            var arIdx = Ar.IndexOf(chars[i]);
            if (arIdx >= 0) chars[i] = Fa[arIdx];
        }
        return new string(chars);
    }

    public static string ToEnglish(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var faIdx = Fa.IndexOf(chars[i]);
            if (faIdx >= 0) { chars[i] = En[faIdx]; continue; }
            var arIdx = Ar.IndexOf(chars[i]);
            if (arIdx >= 0) chars[i] = En[arIdx];
        }
        return new string(chars);
    }

    public static string PersianizeForFormStorage(string? value, Domain.Entities.FieldType fieldType)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        if (value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase)) return value;
        if (fieldType == Domain.Entities.FieldType.Email) return value;
        if (fieldType == Domain.Entities.FieldType.Repeatable) return value;
        return ToPersian(value);
    }
}
