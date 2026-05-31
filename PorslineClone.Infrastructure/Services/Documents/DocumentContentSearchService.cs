using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentContentSearchService(
    AppDbContext db,
    IFarsiTextNormalizer normalizer,
    ILogger<DocumentContentSearchService> logger) : IDocumentContentSearchService
{
    public async Task<(int Total, IReadOnlyList<DocumentContentSearchHit> Items)> SearchAsync(
        string query,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var term = normalizer.Normalize(query);
        if (string.IsNullOrWhiteSpace(term))
            return (0, Array.Empty<DocumentContentSearchHit>());

        if (await TryFullTextSearchAsync(term, skip, take, cancellationToken) is { } fts)
            return fts;

        return await FallbackLikeSearchAsync(term, skip, take, cancellationToken);
    }

    private async Task<(int Total, IReadOnlyList<DocumentContentSearchHit>)?> TryFullTextSearchAsync(
        string term,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        try
        {
            var ftsQuery = BuildContainsQuery(term);
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(cancellationToken);

            const string countSql = """
                SELECT COUNT(DISTINCT d.Id)
                FROM CONTAINSTABLE([dbo].[DocumentVersionTexts], [NormalizedText], @q) AS ft
                INNER JOIN [dbo].[DocumentVersionTexts] t ON t.[DocumentVersionId] = ft.[KEY]
                INNER JOIN [dbo].[DocumentVersions] dv ON dv.[Id] = t.[DocumentVersionId]
                INNER JOIN [dbo].[Documents] d ON d.[Id] = t.[DocumentId]
                WHERE d.[IsDeleted] = 0 AND t.[ProcessingStatus] = 2
                """;

            const string dataSql = """
                SELECT d.[Id], d.[Title], d.[ReferenceNumber], dv.[VersionNumber], dv.[Extension],
                       ft.[Rank] AS [Rank], t.[NormalizedText], t.[ProcessingStatus]
                FROM CONTAINSTABLE([dbo].[DocumentVersionTexts], [NormalizedText], @q) AS ft
                INNER JOIN [dbo].[DocumentVersionTexts] t ON t.[DocumentVersionId] = ft.[KEY]
                INNER JOIN [dbo].[DocumentVersions] dv ON dv.[Id] = t.[DocumentVersionId]
                INNER JOIN [dbo].[Documents] d ON d.[Id] = t.[DocumentId]
                WHERE d.[IsDeleted] = 0 AND t.[ProcessingStatus] = 2
                ORDER BY ft.[Rank] DESC, d.[UpdatedAtUtc] DESC
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
                """;

            await using var countCmd = conn.CreateCommand();
            countCmd.CommandText = countSql;
            AddQueryParam(countCmd, "@q", ftsQuery);
            var totalObj = await countCmd.ExecuteScalarAsync(cancellationToken);
            var total = totalObj is int i ? i : Convert.ToInt32(totalObj ?? 0);

            var items = new List<DocumentContentSearchHit>();
            await using var dataCmd = conn.CreateCommand();
            dataCmd.CommandText = dataSql;
            AddQueryParam(dataCmd, "@q", ftsQuery);
            AddQueryParam(dataCmd, "@skip", skip);
            AddQueryParam(dataCmd, "@take", take);

            await using var reader = await dataCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var body = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var firstToken = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? term;
                items.Add(new DocumentContentSearchHit(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? 0 : Convert.ToDouble(reader.GetValue(5)),
                    BuildSnippet(body, firstToken),
                    "Succeeded"));
            }

            return (total, items);
        }
        catch (SqlException ex) when (ex.Number is 7601 or 7602 or 7609 or 7653)
        {
            logger.LogWarning(ex, "Full-text index unavailable; using LIKE fallback");
            return null;
        }
    }

    private async Task<(int Total, IReadOnlyList<DocumentContentSearchHit>)> FallbackLikeSearchAsync(
        string term,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var like = $"%{term}%";
        var q = db.DocumentVersionTexts.AsNoTracking()
            .Where(x => x.ProcessingStatus == DocumentTextProcessingStatus.Succeeded
                        && x.NormalizedText != null
                        && EF.Functions.Like(x.NormalizedText, like))
            .Join(db.Documents.AsNoTracking(), t => t.DocumentId, d => d.Id, (t, d) => new { t, d })
            .Join(db.DocumentVersions.AsNoTracking(), x => x.t.DocumentVersionId, v => v.Id, (x, v) => new
            {
                x.d.Id,
                x.d.Title,
                x.d.ReferenceNumber,
                v.VersionNumber,
                v.Extension,
                x.t.NormalizedText,
            });

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var firstToken = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? term;
        var items = rows.Select(x => new DocumentContentSearchHit(
            x.Id,
            x.Title,
            x.ReferenceNumber,
            x.VersionNumber,
            x.Extension,
            0,
            BuildSnippet(x.NormalizedText ?? "", firstToken),
            "Succeeded")).ToList();

        return (total, items);
    }

    private static string BuildContainsQuery(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return "\"\"";
        if (tokens.Length == 1) return $"\"{EscapeFts(tokens[0])}\" OR {EscapeFts(tokens[0])}*";
        return string.Join(" AND ", tokens.Select(t => $"\"{EscapeFts(t)}\" OR {EscapeFts(t)}*"));
    }

    private static string EscapeFts(string token)
        => token.Replace("\"", "\"\"");

    private static void AddQueryParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    internal static string BuildSnippet(string body, string term, int radius = 80)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var idx = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return body.Length <= radius * 2 ? body : body[..(radius * 2)] + "…";

        var start = Math.Max(0, idx - radius);
        var len = Math.Min(body.Length - start, term.Length + radius * 2);
        var slice = body.Substring(start, len).Replace('\r', ' ').Replace('\n', ' ');
        return (start > 0 ? "…" : "") + slice.Trim() + (start + len < body.Length ? "…" : "");
    }
}
