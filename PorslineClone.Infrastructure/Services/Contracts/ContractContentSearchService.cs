using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Infrastructure.Services.Contracts;

public sealed class ContractContentSearchService(
    AppDbContext db,
    IPersianTextNormalizer normalizer,
    ILogger<ContractContentSearchService> logger) : IContractContentSearchService
{
    public async Task<(int Total, IReadOnlyList<ContractContentSearchHit> Items)> SearchAsync(
        string query,
        int skip,
        int take,
        IReadOnlyCollection<Guid>? allowedContractIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        ContractStatusDto? status,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var term = normalizer.Normalize(query);
        if (string.IsNullOrWhiteSpace(term))
            return (0, Array.Empty<ContractContentSearchHit>());

        if (await TryFullTextSearchAsync(term, skip, take, allowedContractIds, fromUtc, toUtc, status, cancellationToken) is { } fts)
            return fts;

        return await FallbackLikeSearchAsync(term, skip, take, allowedContractIds, fromUtc, toUtc, status, cancellationToken);
    }

    private async Task<(int Total, IReadOnlyList<ContractContentSearchHit>)?> TryFullTextSearchAsync(
        string term,
        int skip,
        int take,
        IReadOnlyCollection<Guid>? allowedContractIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        ContractStatusDto? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var ftsQuery = FullTextQueryBuilder.BuildContainsQuery(term);
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(cancellationToken);

            var filterSql = BuildFilterSql(allowedContractIds, fromUtc, toUtc, status, out var filterParams);

            var countSql = $"""
                SELECT COUNT(DISTINCT c.[Id])
                FROM CONTAINSTABLE([dbo].[ContractTextIndexes], [NormalizedText], @q) AS ft
                INNER JOIN [dbo].[ContractTextIndexes] t ON t.[ContractId] = ft.[KEY]
                INNER JOIN [dbo].[Contracts] c ON c.[Id] = t.[ContractId]
                WHERE c.[IsSoftDeleted] = 0 AND c.[IndexStatus] = 2
                {filterSql}
                """;

            var dataSql = $"""
                SELECT c.[Id], c.[Title], c.[ContractNumber], c.[IndexStatus],
                       ft.[Rank] AS [Rank], t.[NormalizedText]
                FROM CONTAINSTABLE([dbo].[ContractTextIndexes], [NormalizedText], @q) AS ft
                INNER JOIN [dbo].[ContractTextIndexes] t ON t.[ContractId] = ft.[KEY]
                INNER JOIN [dbo].[Contracts] c ON c.[Id] = t.[ContractId]
                WHERE c.[IsSoftDeleted] = 0 AND c.[IndexStatus] = 2
                {filterSql}
                ORDER BY ft.[Rank] DESC, c.[CreatedAtUtc] DESC
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
                """;

            await using var countCmd = conn.CreateCommand();
            countCmd.CommandText = countSql;
            AddParam(countCmd, "@q", ftsQuery);
            AddFilterParams(countCmd, filterParams);
            var totalObj = await countCmd.ExecuteScalarAsync(cancellationToken);
            var total = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            var items = new List<ContractContentSearchHit>();
            await using var dataCmd = conn.CreateCommand();
            dataCmd.CommandText = dataSql;
            AddParam(dataCmd, "@q", ftsQuery);
            AddParam(dataCmd, "@skip", skip);
            AddParam(dataCmd, "@take", take);
            AddFilterParams(dataCmd, filterParams);

            var firstToken = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? term;
            await using var reader = await dataCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var body = reader.IsDBNull(5) ? "" : reader.GetString(5);
                items.Add(new ContractContentSearchHit(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    MapIndexStatus((ContractIndexStatus)reader.GetInt32(3)),
                    reader.IsDBNull(4) ? 0 : Convert.ToDouble(reader.GetValue(4)),
                    DocumentContentSearchService.BuildSnippet(body, firstToken)));
            }

            return (total, items);
        }
        catch (SqlException ex) when (ex.Number is 7601 or 7602 or 7609 or 7653)
        {
            logger.LogWarning(ex, "Contract FTS unavailable; using LIKE fallback");
            return null;
        }
    }

    private async Task<(int Total, IReadOnlyList<ContractContentSearchHit>)> FallbackLikeSearchAsync(
        string term,
        int skip,
        int take,
        IReadOnlyCollection<Guid>? allowedContractIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        ContractStatusDto? status,
        CancellationToken cancellationToken)
    {
        var like = $"%{term}%";
        var q = db.Contracts.AsNoTracking()
            .Where(c => !c.IsSoftDeleted)
            .Where(c =>
                EF.Functions.Like(c.Title, like)
                || EF.Functions.Like(c.ContractNumber, like)
                || (c.TextIndex != null && c.TextIndex.NormalizedText != null && EF.Functions.Like(c.TextIndex.NormalizedText, like)));

        if (allowedContractIds is { Count: > 0 })
            q = q.Where(c => allowedContractIds.Contains(c.Id));
        if (fromUtc.HasValue)
            q = q.Where(c => c.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            q = q.Where(c => c.CreatedAtUtc <= toUtc.Value);
        if (status.HasValue)
            q = q.Where(c => c.Status == MapStatus(status.Value));

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.ContractNumber,
                c.IndexStatus,
                Body = c.TextIndex != null ? c.TextIndex.NormalizedText : null,
            })
            .ToListAsync(cancellationToken);

        var firstToken = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? term;
        var items = rows.Select(r => new ContractContentSearchHit(
            r.Id,
            r.Title,
            r.ContractNumber,
            MapIndexStatus(r.IndexStatus),
            0,
            DocumentContentSearchService.BuildSnippet(r.Body ?? "", firstToken))).ToList();

        return (total, items);
    }

    private static string BuildFilterSql(
        IReadOnlyCollection<Guid>? allowedContractIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        ContractStatusDto? status,
        out List<(string Name, object Value)> parameters)
    {
        parameters = [];
        var parts = new List<string>();
        if (allowedContractIds is { Count: > 0 })
        {
            parts.Add($"AND c.[Id] IN ({string.Join(",", allowedContractIds.Select((_, i) => $"@id{i}"))})");
            for (var i = 0; i < allowedContractIds.Count; i++)
                parameters.Add(($"@id{i}", allowedContractIds.ElementAt(i)));
        }
        if (fromUtc.HasValue)
        {
            parts.Add("AND c.[CreatedAtUtc] >= @fromUtc");
            parameters.Add(("@fromUtc", fromUtc.Value));
        }
        if (toUtc.HasValue)
        {
            parts.Add("AND c.[CreatedAtUtc] <= @toUtc");
            parameters.Add(("@toUtc", toUtc.Value));
        }
        if (status.HasValue)
        {
            parts.Add("AND c.[Status] = @status");
            parameters.Add(("@status", (int)MapStatus(status.Value)));
        }
        return string.Join(' ', parts);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddFilterParams(System.Data.Common.DbCommand cmd, List<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
            AddParam(cmd, name, value);
    }

    private static ContractIndexStatusDto MapIndexStatus(ContractIndexStatus status) => status switch
    {
        ContractIndexStatus.Pending => ContractIndexStatusDto.Pending,
        ContractIndexStatus.Processing => ContractIndexStatusDto.Processing,
        ContractIndexStatus.Indexed => ContractIndexStatusDto.Indexed,
        ContractIndexStatus.NeedsOcr => ContractIndexStatusDto.NeedsOcr,
        _ => ContractIndexStatusDto.Failed,
    };

    private static ContractStatus MapStatus(ContractStatusDto status) => status switch
    {
        ContractStatusDto.InProgress => ContractStatus.InProgress,
        ContractStatusDto.Approved => ContractStatus.Approved,
        ContractStatusDto.Rejected => ContractStatus.Rejected,
        ContractStatusDto.Suspended => ContractStatus.Suspended,
        ContractStatusDto.Incomplete => ContractStatus.Incomplete,
        _ => ContractStatus.Pending,
    };
}

internal static class FullTextQueryBuilder
{
    public static string BuildContainsQuery(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return "\"\"";
        if (tokens.Length == 1) return $"\"{Escape(tokens[0])}\" OR {Escape(tokens[0])}*";
        return string.Join(" AND ", tokens.Select(t => $"\"{Escape(t)}\" OR {Escape(t)}*"));
    }

    private static string Escape(string token) => token.Replace("\"", "\"\"");
}
