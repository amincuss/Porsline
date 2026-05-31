namespace PorslineClone.Application.FormWordTemplates;

public record FormWordTemplateListItemDto(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string Name,
    int PlaceholderCount,
    bool HasDocx,
    bool HasMappings,
    DateTime UpdatedAtUtc);

public record FormWordFieldMappingDto(
    string PlaceholderKey,
    string? FormFieldLabel,
    string? Source,
    int? ImageWidthPx = null,
    string? FixedValue = null);

public record WordImageOverrideDto(
    Guid SubmissionId,
    string PlaceholderKey,
    string DataUrl,
    int WidthPx);

public record FormWordTemplateDetailDto(
    Guid Id,
    Guid FormId,
    string FormTitle,
    string Name,
    string? DocxFileName,
    IReadOnlyList<string> DetectedPlaceholders,
    IReadOnlyList<FormWordFieldMappingDto> Mappings,
    string? SignaturePlaceholderKey,
    bool HasSignatureImage,
    string? StampPlaceholderKey,
    bool HasStampImage,
    IReadOnlyList<FormWordFormFieldOptionDto> FormFields,
    DateTime UpdatedAtUtc);

public record FormWordFormFieldOptionDto(Guid Id, string Label, int FieldType, string? WordPlaceholderKey = null);

public record FormWordGroupedMemberDto(
    Guid SubmissionId,
    Guid FormId,
    string FormTitle,
    string SubmitterName,
    string? SubmitterMobile,
    string? TrackingCode,
    string SubmittedAtUtc,
    string ApprovalStatus,
    Guid? LatestWordDocumentId,
    string? LatestWordFileName,
    DateTime? LatestWordGeneratedAtUtc);

public record FormWordGroupedGroupDto(
    Guid? GroupId,
    string GroupName,
    IReadOnlyList<FormWordGroupedMemberDto> Members);

public record FormWordGroupedSubmissionsDto(
    IReadOnlyList<FormWordGroupedGroupDto> Groups,
    IReadOnlyList<FormWordTemplateListItemDto> Templates);
