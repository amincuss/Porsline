namespace PorslineClone.Application.Contracts;

public record ContractDocumentTemplateListItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int? ActiveVersionNumber,
    int FieldCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public record ContractDocumentTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    Guid? ActiveVersionId,
    int? ActiveVersionNumber,
    IReadOnlyList<string> DetectedPlaceholders,
    IReadOnlyList<ContractTemplateFieldDto> Fields,
    IReadOnlyList<ContractDocumentTemplateVersionDto> Versions);

public record ContractDocumentTemplateVersionDto(
    Guid Id,
    int VersionNumber,
    string FileName,
    IReadOnlyList<string> DetectedPlaceholders,
    string? ChangeNote,
    DateTime CreatedAtUtc,
    bool IsActive);

public record ContractTemplateFieldDto(
    Guid Id,
    string Key,
    string Label,
    string FieldType,
    bool IsRequired,
    int SortOrder,
    string? DefaultValue,
    string? OptionsJson);

public record UpsertContractTemplateRequest(string Name, string? Description, bool? IsActive);

public record SaveContractTemplateFieldsRequest(IReadOnlyList<ContractTemplateFieldInput> Fields);

public record ContractTemplateFieldInput(
    string Key,
    string Label,
    string FieldType,
    bool IsRequired,
    int SortOrder,
    string? DefaultValue,
    string? OptionsJson);

public record GenerateContractFromTemplateRequest(
    IReadOnlyDictionary<string, string> FieldValues,
    bool ExportPdf = false);

public record ContractDocumentTemplateActiveOptionDto(
    Guid Id,
    string Name,
    IReadOnlyList<ContractTemplateFieldDto> Fields);
