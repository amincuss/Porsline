using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-permissions")]
[Authorize]
public class AdminDocumentPermissionsController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("subjects")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Subjects([FromQuery] string? q, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 100);
        var like = $"%{term}%";

        var usersQ = db.Users.AsNoTracking().Where(x => !x.IsSoftDeleted && x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            usersQ = usersQ.Where(x =>
                EF.Functions.Like(x.FirstName, like) ||
                EF.Functions.Like(x.LastName, like) ||
                EF.Functions.Like((x.FirstName + " " + x.LastName), like) ||
                EF.Functions.Like(x.Email ?? "", like) ||
                EF.Functions.Like(x.PhoneNumber ?? "", like));
        }
        var userRows = await usersQ.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Take(take)
            .Select(x => new
            {
                id = x.Id,
                type = "user",
                x.FirstName,
                x.LastName,
                name = (x.FirstName + " " + x.LastName).Trim(),
                email = x.Email ?? (x.PhoneNumber ?? ""),
                x.AvatarUrl,
            })
            .ToListAsync(ct);
        var users = userRows.Select(u => new
        {
            u.id,
            u.type,
            firstName = u.FirstName,
            lastName = u.LastName,
            u.name,
            u.email,
            avatarUrl = ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, u.id, u.AvatarUrl),
        });

        var rolesQ = db.Roles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(term))
            rolesQ = rolesQ.Where(x => EF.Functions.Like(x.DisplayName ?? "", like) || EF.Functions.Like(x.Name ?? "", like));
        var roles = await rolesQ.OrderBy(x => x.DisplayName).Take(take)
            .Select(x => new
            {
                id = x.Id,
                type = "role",
                firstName = "",
                lastName = "",
                name = (x.DisplayName ?? x.Name ?? "").Trim(),
                email = "",
                avatarUrl = (string?)null,
            })
            .ToListAsync(ct);

        return Ok(users.Concat(roles));
    }

    [HttpGet("{resourceId:guid}")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Get(Guid resourceId, CancellationToken ct)
    {
        var resourceType = await ResolveResourceTypeAsync(resourceId, ct);
        if (resourceType is null) return NotFound(new { message = "منبع یافت نشد" });

        var config = await db.DocumentPermissionConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ResourceType == resourceType.Value && x.ResourceId == resourceId, ct);
        var entries = await db.DocumentPermissionEntries.AsNoTracking()
            .Where(x => x.ResourceType == resourceType.Value && x.ResourceId == resourceId)
            .ToListAsync(ct);

        var userIds = entries.Where(x => x.SubjectType == DocumentPermissionSubjectType.User).Select(x => x.SubjectId).Distinct().ToList();
        var roleIds = entries.Where(x => x.SubjectType == DocumentPermissionSubjectType.Role).Select(x => x.SubjectId).Distinct().ToList();
        var userRows = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                Name = (x.FirstName + " " + x.LastName).Trim(),
                Email = x.Email ?? (x.PhoneNumber ?? ""),
                x.AvatarUrl,
            })
            .ToListAsync(ct);
        var users = userRows.ToDictionary(x => x.Id);
        var roles = await db.Roles.AsNoTracking().Where(x => roleIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = (x.DisplayName ?? x.Name ?? "").Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return Ok(new
        {
            resourceType = resourceType.Value.ToString(),
            inheritFromParent = config?.InheritFromParent ?? true,
            entries = entries.Select(x =>
            {
                if (x.SubjectType == DocumentPermissionSubjectType.User
                    && users.TryGetValue(x.SubjectId, out var u))
                {
                    return new
                    {
                        id = x.Id,
                        subjectId = x.SubjectId,
                        subjectType = "user",
                        name = string.IsNullOrWhiteSpace(u.Name) ? "کاربر" : u.Name,
                        firstName = u.FirstName ?? "",
                        lastName = u.LastName ?? "",
                        email = u.Email,
                        avatarUrl = ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, u.Id, u.AvatarUrl),
                        level = x.Level.ToString(),
                    };
                }

                return new
                {
                    id = x.Id,
                    subjectId = x.SubjectId,
                    subjectType = x.SubjectType == DocumentPermissionSubjectType.User ? "user" : "role",
                    name = x.SubjectType == DocumentPermissionSubjectType.User
                        ? "کاربر"
                        : roles.GetValueOrDefault(x.SubjectId, "نقش"),
                    firstName = "",
                    lastName = "",
                    email = "",
                    avatarUrl = (string?)null,
                    level = x.Level.ToString(),
                };
            }),
        });
    }

    [HttpPut("{resourceId:guid}")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> Put(Guid resourceId, [FromBody] SavePermissionsRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resourceType = await ResolveResourceTypeAsync(resourceId, ct);
        if (resourceType is null) return NotFound(new { message = "منبع یافت نشد" });

        var normalized = (req.Entries ?? [])
            .Where(x => x.SubjectId != Guid.Empty)
            .Select(x => new
            {
                x.SubjectId,
                SubjectType = ParseSubjectType(x.SubjectType),
                Level = ParsePermissionLevel(x.Level),
            })
            .DistinctBy(x => new { x.SubjectId, x.SubjectType })
            .ToList();

        if (!req.InheritFromParent)
        {
            var ownerCount = normalized.Count(x => x.Level == DocumentPermissionLevel.Owner);
            if (ownerCount == 0)
                return BadRequest(new { message = "حداقل یک Owner باید تعریف شود" });
        }

        var config = await db.DocumentPermissionConfigs
            .FirstOrDefaultAsync(x => x.ResourceType == resourceType.Value && x.ResourceId == resourceId, ct);
        if (config is null)
        {
            config = new DocumentPermissionConfig
            {
                ResourceType = resourceType.Value,
                ResourceId = resourceId,
            };
            db.DocumentPermissionConfigs.Add(config);
        }
        config.InheritFromParent = req.InheritFromParent;
        config.UpdatedByUserId = userId;
        config.UpdatedAtUtc = DateTime.UtcNow;

        var existing = await db.DocumentPermissionEntries
            .Where(x => x.ResourceType == resourceType.Value && x.ResourceId == resourceId)
            .ToListAsync(ct);
        db.DocumentPermissionEntries.RemoveRange(existing);
        db.DocumentPermissionEntries.AddRange(normalized.Select(x => new DocumentPermissionEntry
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType.Value,
            ResourceId = resourceId,
            SubjectType = x.SubjectType,
            SubjectId = x.SubjectId,
            Level = x.Level,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        }));
        if (resourceType.Value == DocumentNodeType.File)
        {
            db.DocumentActivities.Add(new DocumentActivity
            {
                Id = Guid.NewGuid(),
                DocumentId = resourceId,
                EventType = "Share/Permission Change",
                Message = "تنظیمات دسترسی سند بروزرسانی شد",
                ActorUserId = userId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()[..Math.Min(500, Request.Headers.UserAgent.ToString().Length)],
                NewValuesJson = JsonSerializer.Serialize(new { req.InheritFromParent, entries = normalized.Count }),
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "مجوزها ذخیره شد" });
    }

    [HttpPost("{resourceId:guid}/share-links")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> CreateShareLink(Guid resourceId, [FromBody] CreateShareLinkRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resourceType = await ResolveResourceTypeAsync(resourceId, ct);
        if (resourceType is null) return NotFound(new { message = "منبع یافت نشد" });

        var token = await GenerateUniqueTokenAsync(ct);
        var scope = ParseShareScope(req.Scope);
        var link = new DocumentShareLink
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType.Value,
            ResourceId = resourceId,
            Scope = scope,
            Token = token,
            SpecificSubjectIdsJson = scope == DocumentShareScope.SpecificUsers
                ? JsonSerializer.Serialize(req.SpecificSubjectIds ?? [])
                : null,
            ExpiresAtUtc = req.ExpiresAtUtc,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
        };
        db.DocumentShareLinks.Add(link);
        if (resourceType.Value == DocumentNodeType.File)
        {
            db.DocumentActivities.Add(new DocumentActivity
            {
                Id = Guid.NewGuid(),
                DocumentId = resourceId,
                EventType = "Share",
                Message = "لینک اشتراک سند ایجاد شد",
                ActorUserId = userId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()[..Math.Min(500, Request.Headers.UserAgent.ToString().Length)],
                NewValuesJson = JsonSerializer.Serialize(new { scope = scope.ToString(), req.ExpiresAtUtc }),
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new { url = $"{baseUrl}/api/public/document-shares/access?t={token}" });
    }

    private async Task<DocumentNodeType?> ResolveResourceTypeAsync(Guid resourceId, CancellationToken ct)
    {
        if (await db.Documents.AsNoTracking().AnyAsync(x => x.Id == resourceId, ct))
            return DocumentNodeType.File;
        if (await db.DocumentFolders.AsNoTracking().AnyAsync(x => x.Id == resourceId, ct))
            return DocumentNodeType.Folder;
        return null;
    }

    private static DocumentPermissionSubjectType ParseSubjectType(string? value)
        => (value ?? "").Trim().ToLowerInvariant() == "role" ? DocumentPermissionSubjectType.Role : DocumentPermissionSubjectType.User;

    private static DocumentPermissionLevel ParsePermissionLevel(string? value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "editor" => DocumentPermissionLevel.Editor,
            "manager" => DocumentPermissionLevel.Manager,
            "owner" => DocumentPermissionLevel.Owner,
            _ => DocumentPermissionLevel.Viewer,
        };

    private static DocumentShareScope ParseShareScope(string? value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "link_anyone" => DocumentShareScope.AnyoneWithLink,
            "specific_users" => DocumentShareScope.SpecificUsers,
            _ => DocumentShareScope.OrganizationOnly,
        };

    private async Task<string> GenerateUniqueTokenAsync(CancellationToken ct)
    {
        while (true)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
            var exists = await db.DocumentShareLinks.AsNoTracking().AnyAsync(x => x.Token == token, ct);
            if (!exists) return token;
        }
    }

    private bool TryGetUserId(out Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out id);
    }
}

public sealed class SavePermissionsRequest
{
    public bool InheritFromParent { get; set; } = true;
    public List<SavePermissionEntryRequest> Entries { get; set; } = [];
}

public sealed class SavePermissionEntryRequest
{
    public Guid SubjectId { get; set; }
    public string? SubjectType { get; set; }
    public string? Level { get; set; }
}

public sealed class CreateShareLinkRequest
{
    public string? Scope { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<Guid>? SpecificSubjectIds { get; set; }
}
