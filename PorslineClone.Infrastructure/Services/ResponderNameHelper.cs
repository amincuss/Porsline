namespace PorslineClone.Infrastructure.Services;

public static class ResponderNameHelper
{
    public static (string FirstName, string LastName, string FullName) SplitFullName(string? fullName)
    {
        var parts = (fullName ?? "")
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return ("", "", "");
        if (parts.Length == 1)
            return (parts[0], "", parts[0]);
        var first = parts[0];
        var last = string.Join(" ", parts.Skip(1));
        return (first, last, $"{first} {last}".Trim());
    }
}
