namespace PorslineClone.Application.Contracts;

public record DashboardFeedItemDto(
    string Id,
    string ServiceKey,
    string Title,
    string Message,
    DateTime AtUtc,
    string? LinkRoute);

public record DashboardFeedDto(
    IReadOnlyList<DashboardFeedItemDto> Contracts,
    IReadOnlyList<DashboardFeedItemDto> Forms,
    IReadOnlyList<DashboardFeedItemDto> MyPending);

public record DashboardQuickSearchResultDto(
    IReadOnlyList<DashboardQuickSearchItemDto> Items);

public record DashboardQuickSearchItemDto(
    string Id,
    string Kind,
    string Title,
    string Subtitle,
    string Status,
    string PartyName,
    string? NationalId,
    string? Phone,
    string? ContractNumber,
    string? WorkflowName,
    DateTime AtUtc,
    string LinkRoute,
    bool HasFile,
    bool HasSignedDocument,
    bool HasOriginalDocument,
    string? FileName,
    IReadOnlyList<ApprovalStepDto> Steps);
