using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/documents/audit")]
[Authorize(Policy = "forms.read")]
public class AdminDocumentAuditController(AppDbContext db) : ControllerBase
{
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] AuditQueryRequest req, CancellationToken ct)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 10, 200);
        var q = db.DocumentActivities.AsNoTracking().AsQueryable();

        if (req.UserId.HasValue)
            q = q.Where(x => x.ActorUserId == req.UserId.Value);
        if (req.FromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= req.FromUtc.Value);
        if (req.ToUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc <= req.ToUtc.Value);
        if ((req.Actions ?? []).Count > 0)
        {
            var actions = req.Actions!.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            q = q.Where(x => actions.Contains(x.EventType));
        }
        if (!string.IsNullOrWhiteSpace(req.Query))
        {
            var like = $"%{req.Query.Trim()}%";
            q = q.Where(x =>
                EF.Functions.Like(x.Message, like) ||
                EF.Functions.Like(x.IpAddress ?? "", like) ||
                EF.Functions.Like(x.Reason ?? "", like));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.DocumentId,
                x.EventType,
                x.Message,
                x.ActorUserId,
                x.IpAddress,
                x.CreatedAtUtc,
                x.UserAgent,
                x.Reason,
            })
            .ToListAsync(ct);

        var userIds = rows.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim(), x.AvatarUrl })
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.AvatarUrl }, ct);
        var docIds = rows.Select(x => x.DocumentId).Distinct().ToList();
        var docs = await db.Documents.AsNoTracking()
            .Where(x => docIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);

        return Ok(new
        {
            total,
            page,
            pageSize,
            items = rows.Select(x =>
            {
                var user = x.ActorUserId.HasValue && users.TryGetValue(x.ActorUserId.Value, out var u) ? u : null;
                return new
                {
                    x.Id,
                    action = x.EventType,
                    x.Message,
                    x.DocumentId,
                    resourceTitle = docs.GetValueOrDefault(x.DocumentId, "Document"),
                    userId = x.ActorUserId,
                    userName = user?.Name ?? "System",
                    avatarUrl = user?.AvatarUrl,
                    x.IpAddress,
                    x.CreatedAtUtc,
                };
            }),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var row = await db.DocumentActivities.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.DocumentId,
                x.EventType,
                x.Message,
                x.ActorUserId,
                x.IpAddress,
                x.UserAgent,
                x.CreatedAtUtc,
                x.Reason,
                x.OldValuesJson,
                x.NewValuesJson,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return NotFound(new { message = "لاگ یافت نشد" });

        var userName = "";
        if (row.ActorUserId.HasValue)
        {
            userName = await db.Users.AsNoTracking()
                .Where(x => x.Id == row.ActorUserId.Value)
                .Select(x => (x.FirstName + " " + x.LastName).Trim())
                .FirstOrDefaultAsync(ct) ?? "";
        }
        var resourceTitle = await db.Documents.AsNoTracking()
            .Where(x => x.Id == row.DocumentId)
            .Select(x => x.Title)
            .FirstOrDefaultAsync(ct) ?? "Document";

        return Ok(new
        {
            row.Id,
            row.DocumentId,
            resourceTitle,
            action = row.EventType,
            row.Message,
            userId = row.ActorUserId,
            userName,
            row.IpAddress,
            row.UserAgent,
            row.CreatedAtUtc,
            row.Reason,
            oldValuesJson = row.OldValuesJson,
            newValuesJson = row.NewValuesJson,
        });
    }

    [HttpPost("export/csv")]
    public async Task<IActionResult> ExportCsv([FromBody] AuditQueryRequest req, CancellationToken ct)
    {
        req.Page = 1;
        req.PageSize = 2000;
        var q = db.DocumentActivities.AsNoTracking().AsQueryable();
        if (req.UserId.HasValue) q = q.Where(x => x.ActorUserId == req.UserId.Value);
        if (req.FromUtc.HasValue) q = q.Where(x => x.CreatedAtUtc >= req.FromUtc.Value);
        if (req.ToUtc.HasValue) q = q.Where(x => x.CreatedAtUtc <= req.ToUtc.Value);
        if ((req.Actions ?? []).Count > 0)
        {
            var actions = req.Actions!.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            q = q.Where(x => actions.Contains(x.EventType));
        }
        if (!string.IsNullOrWhiteSpace(req.Query))
        {
            var like = $"%{req.Query.Trim()}%";
            q = q.Where(x =>
                EF.Functions.Like(x.Message, like) ||
                EF.Functions.Like(x.IpAddress ?? "", like) ||
                EF.Functions.Like(x.Reason ?? "", like));
        }
        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).Take(2000).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,Action,DocumentId,Message,ActorUserId,IpAddress,CreatedAtUtc");
        foreach (var x in rows)
        {
            static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                Esc(x.Id.ToString()),
                Esc(x.EventType),
                Esc(x.DocumentId.ToString()),
                Esc(x.Message),
                Esc(x.ActorUserId?.ToString()),
                Esc(x.IpAddress),
                Esc(x.CreatedAtUtc.ToString("O"))));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"audit-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}

public sealed class AuditQueryRequest
{
    public Guid? UserId { get; set; }
    public List<string>? Actions { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
