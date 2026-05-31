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

public record DashboardKpiDto(
    string Key,
    string Label,
    long Value,
    string? Hint,
    string? LinkRoute,
    string Icon);

public record DashboardTaskItemDto(
    string Id,
    string Kind,
    string Title,
    string TaskType,
    string Status,
    string StatusLabel,
    DateTime? DueAtUtc,
    bool IsOverdue,
    string? Category,
    string? Priority,
    string LinkRoute);

public record DashboardActivityItemDto(
    string Id,
    string Kind,
    string ActionType,
    string Title,
    string Message,
    string? ActorName,
    DateTime AtUtc,
    string LinkRoute);

public record DashboardSummaryDto(
    string? UserDisplayName,
    IReadOnlyList<DashboardKpiDto> Kpis,
    IReadOnlyList<DashboardTaskItemDto> MyTasks,
    IReadOnlyList<DashboardTaskItemDto> PendingApprovals,
    IReadOnlyList<DashboardActivityItemDto> RecentActivity,
    int MyTasksTotal,
    int PendingApprovalsTotal);
