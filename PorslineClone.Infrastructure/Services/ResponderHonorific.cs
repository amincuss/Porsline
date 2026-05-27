using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class ResponderHonorific
{
    public static UserGender? ParseGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim().ToLowerInvariant();
        return t switch
        {
            "male" or "m" or "1" or "آقا" or "آقای" or "mr" => UserGender.Male,
            "female" or "f" or "2" or "خانم" or "ms" => UserGender.Female,
            _ => null,
        };
    }

    public static string GenderLabel(UserGender? gender) =>
        gender == UserGender.Female ? "خانم" : "آقای";

    /// <summary>مثلاً «خانم مریم احمدی» یا «آقای علی رضایی»</summary>
    public static string FormatFullName(string? fullName, UserGender? gender)
    {
        var name = (fullName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return "پاسخگو";
        if (name.StartsWith("آقای ", StringComparison.Ordinal) || name.StartsWith("خانم ", StringComparison.Ordinal))
            return name;
        if (gender is null) return name;
        return $"{GenderLabel(gender)} {name}";
    }
}
