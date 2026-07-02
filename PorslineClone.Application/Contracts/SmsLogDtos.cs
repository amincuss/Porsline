namespace PorslineClone.Application.Contracts;

public record SmsLogItemDto(
    Guid Id,
    string MobileNumber,
    string Message,
    bool IsSuccess,
    string? ErrorMessage,
    string? TechnicalDetail,
    string? Source,
    int? HttpStatusCode,
    DateTime CreatedAtUtc);

public record SmsLogListResponse(
    IReadOnlyList<SmsLogItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    int SuccessCount,
    int FailedCount);

public record SmsLogResendResponse(
    bool IsSuccess,
    string Message,
    string? ErrorMessage,
    Guid? NewLogId);
