namespace PorslineClone.Application.Abstractions;

public record SmsLogEntry(
    string MobileNumber,
    string Message,
    bool IsSuccess,
    string? ErrorMessage,
    string? TechnicalDetail,
    string? Source,
    int? HttpStatusCode);

public interface ISmsLogService
{
    Task LogAsync(SmsLogEntry entry, CancellationToken ct = default);
    Task UpdateAsync(Guid logId, SmsLogEntry entry, CancellationToken ct = default);
}
