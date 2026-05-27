using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/messages")]
[Authorize]
public class AdminMessagesController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxAttachmentBytes = 15 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAttachmentExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".webp", ".zip", ".rar",
    };

    [HttpGet("stats")]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var inbox = db.InboxMessages.Where(x => x.UserId == userId);
        var inboxTotal = await inbox.CountAsync(ct);
        var unread = await inbox.CountAsync(x => !x.IsRead && !x.IsArchived, ct);
        var archived = await inbox.CountAsync(x => x.IsArchived, ct);
        var systemUnread = await inbox.CountAsync(x => !x.IsRead && !x.IsArchived && x.SenderUserId == null, ct);
        var personalUnread = await inbox.CountAsync(x => !x.IsRead && !x.IsArchived && x.SenderUserId != null, ct);

        var sent = db.InboxMessages.Where(x => x.SenderUserId == userId);
        var sentTotal = await sent.CountAsync(ct);
        var sentUnreadByRecipient = await sent.CountAsync(x => !x.IsRead, ct);

        return Ok(new AdminInboxStatsDto(
            inboxTotal, unread, archived, systemUnread, personalUnread, sentTotal, sentUnreadByRecipient));
    }

    [HttpGet("recipients")]
    [Authorize(Policy = "messages.send")]
    public async Task<IActionResult> Recipients([FromQuery] string? q, [FromQuery] int take = 40, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var selfId)) return Unauthorized();
        take = Math.Clamp(take, 1, 80);
        var term = (q ?? "").Trim();

        var query = db.Users.AsNoTracking()
            .Where(x => !x.IsSoftDeleted && x.IsActive && x.Id != selfId);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            query = query.Where(x =>
                EF.Functions.Like(x.FirstName, like)
                || EF.Functions.Like(x.LastName, like)
                || EF.Functions.Like((x.FirstName + " " + x.LastName), like)
                || EF.Functions.Like(x.Email ?? "", like)
                || EF.Functions.Like(x.PhoneNumber ?? "", like));
        }

        var users = await query
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                Email = x.Email ?? (x.PhoneNumber ?? ""),
                x.AvatarUrl,
            })
            .ToListAsync(ct);

        return Ok(users.Select(u => new MessageRecipientOptionDto(
            u.Id,
            u.FirstName ?? "",
            u.LastName ?? "",
            $"{u.FirstName} {u.LastName}".Trim(),
            u.Email,
            ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, u.Id, u.AvatarUrl))));
    }

    [HttpGet]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> List(
        [FromQuery] string tab = "system",
        [FromQuery] string? folder = "inbox",
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var canReadAll = User.HasClaim("permission", "messages.read.all");

        IQueryable<InboxMessage> q = tab.ToLowerInvariant() switch
        {
            "sent" => db.InboxMessages.Where(x => x.SenderUserId == userId),
            "all" when canReadAll => db.InboxMessages,
            "user" => db.InboxMessages.Where(x => x.UserId == userId && x.SenderUserId != null),
            "system" => db.InboxMessages.Where(x => x.UserId == userId && x.SenderUserId == null),
            _ => db.InboxMessages.Where(x => x.UserId == userId),
        };

        if (tab.Equals("sent", StringComparison.OrdinalIgnoreCase))
        {
            q = (folder ?? "inbox").ToLowerInvariant() switch
            {
                "archived" => q.Where(x => x.IsArchived),
                "all" => q,
                _ => q.Where(x => !x.IsArchived),
            };
        }
        else if (!tab.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            q = (folder ?? "inbox").ToLowerInvariant() switch
            {
                "archived" => q.Where(x => x.IsArchived),
                "all" => q,
                _ => q.Where(x => !x.IsArchived),
            };
        }

        if (tab.Equals("all", StringComparison.OrdinalIgnoreCase) && !canReadAll)
            return Forbid();

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var userIds = rows
            .SelectMany(x => new[] { x.UserId, x.SenderUserId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                Email = u.Email ?? (u.PhoneNumber ?? ""),
            })
            .ToDictionaryAsync(u => u.Id, ct);

        string? Name(Guid? id)
        {
            if (id is null || !users.TryGetValue(id.Value, out var u)) return null;
            var n = $"{u.FirstName} {u.LastName}".Trim();
            return string.IsNullOrWhiteSpace(n) ? null : n;
        }

        string? Email(Guid? id)
        {
            if (id is null || !users.TryGetValue(id.Value, out var u)) return null;
            return string.IsNullOrWhiteSpace(u.Email) ? null : u.Email;
        }

        var items = rows.Select(x => new AdminInboxMessageDto(
            x.Id,
            x.Title,
            x.Body,
            x.IsHtml,
            x.IsRead,
            x.IsArchived,
            x.CreatedAtUtc,
            x.ReadAtUtc,
            x.SenderUserId is null,
            x.SenderUserId,
            Name(x.SenderUserId) ?? (x.SenderUserId is null ? "سیستم" : null),
            x.SenderUserId is null ? null : Email(x.SenderUserId),
            x.UserId,
            Name(x.UserId),
            Email(x.UserId),
            !string.IsNullOrWhiteSpace(x.AttachmentPath),
            x.AttachmentFileName)).ToList();

        return Ok(items);
    }

    [HttpPost("send")]
    [Authorize(Policy = "messages.send")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Send([FromForm] SendAdminMessageForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var senderId)) return Unauthorized();
        if (form.RecipientUserId == Guid.Empty)
            return BadRequest(new { message = "گیرنده را انتخاب کنید" });

        var title = (form.Title ?? "").Trim();
        var body = (form.Body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان پیام الزامی است" });
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(new { message = "متن پیام الزامی است" });
        if (title.Length > 200)
            return BadRequest(new { message = "عنوان حداکثر ۲۰۰ کاراکتر است" });

        var recipient = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == form.RecipientUserId && !x.IsSoftDeleted && x.IsActive, ct);
        if (recipient is null)
            return BadRequest(new { message = "گیرنده یافت نشد" });
        if (recipient.Id == senderId)
            return BadRequest(new { message = "ارسال پیام به خودتان مجاز نیست" });

        var messageId = Guid.NewGuid();
        string? attachmentFileName = null;
        string? attachmentPath = null;

        var file = form.Attachment ?? form.File;
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxAttachmentBytes)
                return BadRequest(new { message = "حداکثر حجم پیوست ۱۵ مگابایت است" });
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedAttachmentExt.Contains(ext))
                return BadRequest(new { message = "نوع فایل پیوست مجاز نیست" });

            var safeName = $"{messageId:N}{ext.ToLowerInvariant()}";
            var dir = Path.Combine(env.ContentRootPath, "InboxAttachments", messageId.ToString("N"));
            Directory.CreateDirectory(dir);
            var fullPath = Path.Combine(dir, safeName);
            await using (var stream = System.IO.File.Create(fullPath))
                await file.CopyToAsync(stream, ct);

            attachmentFileName = Path.GetFileName(file.FileName);
            attachmentPath = $"InboxAttachments/{messageId:N}/{safeName}";
        }

        db.InboxMessages.Add(new InboxMessage
        {
            Id = messageId,
            UserId = form.RecipientUserId,
            SenderUserId = senderId,
            Title = title,
            Body = body,
            IsHtml = form.IsHtml,
            AttachmentFileName = attachmentFileName,
            AttachmentPath = attachmentPath,
            IsRead = false,
            IsArchived = false,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "پیام ارسال شد", id = messageId });
    }

    [HttpGet("{messageId:guid}/attachment")]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> DownloadAttachment(Guid messageId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var msg = await db.InboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (msg is null || string.IsNullOrWhiteSpace(msg.AttachmentPath))
            return NotFound(new { message = "پیوست یافت نشد" });

        var canReadAll = User.HasClaim("permission", "messages.read.all");
        if (msg.UserId != userId && msg.SenderUserId != userId && !canReadAll)
            return Forbid();

        var fullPath = Path.Combine(env.ContentRootPath, msg.AttachmentPath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "فایل پیوست موجود نیست" });

        var downloadName = string.IsNullOrWhiteSpace(msg.AttachmentFileName)
            ? Path.GetFileName(fullPath)
            : msg.AttachmentFileName;
        return PhysicalFile(fullPath, "application/octet-stream", downloadName);
    }

    [HttpPatch("{messageId:guid}/read")]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> MarkRead(Guid messageId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == userId, ct);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });

        if (!msg.IsRead)
        {
            msg.IsRead = true;
            msg.ReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { message = "پیام خوانده شد" });
    }

    [HttpPatch("{messageId:guid}/archive")]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> Archive(Guid messageId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == userId, ct);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });

        msg.IsArchived = true;
        if (!msg.IsRead)
        {
            msg.IsRead = true;
            msg.ReadAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "پیام بایگانی شد" });
    }

    [HttpDelete("{messageId:guid}")]
    [Authorize(Policy = "messages.read")]
    public async Task<IActionResult> Delete(Guid messageId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var canReadAll = User.HasClaim("permission", "messages.read.all");

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });
        if (msg.UserId != userId && msg.SenderUserId != userId && !canReadAll)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(msg.AttachmentPath))
        {
            var fullPath = Path.Combine(env.ContentRootPath, msg.AttachmentPath.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                try { System.IO.File.Delete(fullPath); } catch { /* ignore */ }
            }
        }

        db.InboxMessages.Remove(msg);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پیام حذف شد" });
    }

    private bool TryGetUserId(out Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out id);
    }
}

public sealed class SendAdminMessageForm
{
    public Guid RecipientUserId { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public bool IsHtml { get; set; }
    public IFormFile? Attachment { get; set; }
    public IFormFile? File { get; set; }
}
