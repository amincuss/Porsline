namespace PorslineClone.Application.FormSubmissions;

public record FormSubmissionExcelExportFieldOption(
    string Key,
    string Label,
    bool IsMeta,
    bool IsFile,
    int FilledCount);

public record FormSubmissionExcelExportFormOption(
    Guid FormId,
    string FormTitle,
    int SubmissionCount,
    IReadOnlyList<FormSubmissionExcelExportFieldOption> Fields);

public record FormSubmissionExcelExportOptionsDto(
    int TotalSubmissions,
    IReadOnlyList<FormSubmissionExcelExportFormOption> Forms);

public record StartFormSubmissionExcelExportRequest(
    Guid? GroupId,
    bool UngroupedOnly,
    Guid FormId,
    List<string> SelectedFieldKeys);

public record StartFormSubmissionExcelExportResponse(Guid JobId, string Message);

public record FormSubmissionExcelExportStatusDto(
    Guid Id,
    string Status,
    int TotalCount,
    int ProcessedCount,
    string? FileName,
    string? DownloadUrl,
    long? FileSizeBytes,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
