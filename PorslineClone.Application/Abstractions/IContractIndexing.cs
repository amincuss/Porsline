namespace PorslineClone.Application.Abstractions;

public interface IPersianTextNormalizer
{
    string Normalize(string? input);
}

public interface IContractExtractAndIndexJob
{
    Task ExtractAndIndexAsync(Guid contractId, bool force = false, CancellationToken cancellationToken = default);
}

public interface IContractIndexEnqueue
{
    void EnqueueExtractAndIndex(Guid contractId, bool force = false);
}

public sealed record ContractContentSearchHit(
    Guid ContractId,
    string Title,
    string ContractNumber,
    ContractIndexStatusDto IndexStatus,
    double Rank,
    string Snippet);

public enum ContractIndexStatusDto
{
    Pending,
    Processing,
    Indexed,
    Failed,
    NeedsOcr,
}

public interface IContractContentSearchService
{
    Task<(int Total, IReadOnlyList<ContractContentSearchHit> Items)> SearchAsync(
        string query,
        int skip,
        int take,
        IReadOnlyCollection<Guid>? allowedContractIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        ContractStatusDto? status,
        CancellationToken cancellationToken = default);
}

public enum ContractStatusDto
{
    Pending,
    InProgress,
    Approved,
    Rejected,
    Suspended,
    Incomplete,
}
