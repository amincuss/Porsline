namespace PorslineClone.Application.Contracts;

public record SmsGatewayStatusDto(
    bool IsConfigured,
    string? UrlAddress,
    string? CallerId,
    bool PasswordConfigured,
    IReadOnlyList<string> ConfigurationIssues);

public record SmsTestPatternOptionDto(
    string Key,
    string Title,
    string Category,
    IReadOnlyList<SmsPatternPlaceholderDto> Placeholders);

public record SmsTestPreviewRequest(
    string? PatternKey,
    string? Message,
    Dictionary<string, string?>? PatternVars);

public record SmsTestPreviewResponse(
    string RenderedMessage,
    string Mode);

public record SmsTestSendRequest(
    string MobileNumber,
    string? Message,
    string? PatternKey,
    Dictionary<string, string?>? PatternVars);

public record SmsTestSendResponse(
    bool IsSuccess,
    string Message,
    string? ErrorMessage,
    string RenderedMessage,
    Guid? LogId,
    int? HttpStatusCode);
