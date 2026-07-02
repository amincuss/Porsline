namespace PorslineClone.Application.Contracts;

public record SmsPatternPlaceholderDto(string Key, string Label, string? Sample = null);

public record SmsPatternDto(
    Guid Id,
    string Key,
    string Title,
    string Category,
    string Icon,
    string IconColor,
    string Template,
    IReadOnlyList<SmsPatternPlaceholderDto> Placeholders,
    string? Description,
    int SortOrder,
    bool IsActive,
    DateTime UpdatedAtUtc);

public record UpdateSmsPatternItem(string Key, string Template);

public record UpdateSmsPatternsRequest(IReadOnlyList<UpdateSmsPatternItem> Patterns);

public record SmsPatternCategoryDto(
    string Key,
    string Title,
    string Icon,
    string IconColor,
    IReadOnlyList<SmsPatternDto> Patterns);
