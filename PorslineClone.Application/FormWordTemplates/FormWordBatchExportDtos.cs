namespace PorslineClone.Application.FormWordTemplates;

public record FormWordBatchExportStatusDto(
    Guid Id,
    string Status,
    int TotalCount,
    int ProcessedCount,
    string? ZipFileName,
    string? DownloadUrl,
    long? ZipSizeBytes,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public record StartFormWordBatchExportRequest(
    Guid TemplateId,
    List<Guid>? SubmissionIds,
    List<WordImageOverrideDto>? ImageOverrides = null);

public record StartFormWordBatchExportResponse(Guid JobId, string Message);
