using System.Globalization;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Api.Helpers;
using PorslineClone.Api.Http;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/documents")]
[Authorize(Policy = "forms.read")]
public class AdminDocumentsController(
    AppDbContext db,
    IDocumentVersionFileAccess files,
    ILibreOfficeDocumentService libreOffice,
    IDocumentTextExtractionQueue textExtractionQueue,
    IDocumentContentSearchService contentSearch,
    DocumentWorkflowProcessor workflowProcessor,
    DocumentWorkflowAssignService workflowAssignService,
    DocumentLifecycleService lifecycleService) : ControllerBase
{
    private const long MaxUploadBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".zip",
    };

    [HttpGet("tree")]
    public async Task<IActionResult> Tree(CancellationToken ct)
    {
        await EnsureRootFolderAsync(ct);

        var folders = await db.DocumentFolders.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.ParentId, x.CreatedAtUtc })
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var fileCounts = await WhereVisibleInExplorer(db.Documents.AsNoTracking())
            .GroupBy(x => x.FolderId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        return Ok(folders.Select(x => new
        {
            x.Id,
            x.Name,
            x.ParentId,
            fileCount = fileCounts.TryGetValue(x.Id, out var c) ? c : 0,
        }));
    }

    [HttpGet("explorer")]
    public async Task<IActionResult> Explorer([FromQuery] Guid? folderId, [FromQuery] string? q, CancellationToken ct)
    {
        var search = (q ?? "").Trim();
        var effectiveFolderId = folderId ?? await EnsureRootFolderAsync(ct);

        var childFoldersQ = db.DocumentFolders.AsNoTracking()
            .Where(x => x.ParentId == effectiveFolderId);
        var filesQ = WhereVisibleInExplorer(db.Documents.AsNoTracking())
            .Where(x => x.FolderId == effectiveFolderId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search}%";
            childFoldersQ = childFoldersQ.Where(x => EF.Functions.Like(x.Name, like));
            filesQ = filesQ.Where(x =>
                EF.Functions.Like(x.Title, like)
                || EF.Functions.Like(x.Category, like)
                || EF.Functions.Like(x.ReferenceNumber ?? "", like)
                || x.Versions.Any(v => EF.Functions.Like(v.OriginalFileName, like)));
        }

        var childFolders = await childFoldersQ
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ParentId, x.CreatedAtUtc })
            .ToListAsync(ct);
        var files = await filesQ
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.ReferenceNumber,
                x.AccessLevel,
                x.OwnerUserId,
                x.OrganizationalUnitId,
                x.ProjectId,
                organizationalUnitName = x.OrganizationalUnit != null ? x.OrganizationalUnit.Name : null,
                projectName = x.Project != null ? x.Project.Name : null,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                Latest = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new
                {
                    v.Extension,
                    v.SizeBytes,
                }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var ownerIds = files.Select(x => x.OwnerUserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking()
            .Where(x => ownerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FirstName, x.LastName })
            .ToDictionaryAsync(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim(), ct);

        return Ok(new
        {
            folderId = effectiveFolderId,
            folders = childFolders.Select(x => new
            {
                id = x.Id,
                name = x.Name,
                type = "folder",
                parentId = x.ParentId,
                lastModified = x.CreatedAtUtc,
            }),
            files = files.Select(x => new
            {
                id = x.Id,
                name = x.Title,
                type = "file",
                ext = x.Latest?.Extension,
                size = x.Latest?.SizeBytes ?? 0,
                category = x.Category,
                organizationalUnitId = x.OrganizationalUnitId,
                organizationalUnitName = x.organizationalUnitName,
                projectId = x.ProjectId,
                projectName = x.projectName,
                referenceNumber = x.ReferenceNumber,
                accessLevel = x.AccessLevel.ToString(),
                owner = owners.TryGetValue(x.OwnerUserId, out var ownerName) && !string.IsNullOrWhiteSpace(ownerName)
                    ? ownerName
                    : "—",
                createdAtUtc = x.CreatedAtUtc,
                lastModified = x.UpdatedAtUtc,
            }),
        });
    }

    [HttpGet("owners")]
    public async Task<IActionResult> Owners([FromQuery] string? q, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 100);
        var usersQ = db.Users.AsNoTracking().Where(x => !x.IsSoftDeleted && x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            usersQ = usersQ.Where(x =>
                EF.Functions.Like(x.FirstName, like) ||
                EF.Functions.Like(x.LastName, like) ||
                EF.Functions.Like((x.FirstName + " " + x.LastName), like));
        }
        var users = await usersQ
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Take(take)
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() })
            .ToListAsync(ct);
        return Ok(users);
    }

    [HttpGet("settings/tags")]
    public async Task<IActionResult> GetSystemTags([FromQuery] string? q, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 200);
        var tagsQ = db.DocumentSystemTags.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            tagsQ = tagsQ.Where(x => EF.Functions.Like(x.Name, like));
        }

        var items = await tagsQ
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("settings/tags")]
    public async Task<IActionResult> CreateSystemTag([FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام برچسب الزامی است" });
        if (name.Length > 80)
            return BadRequest(new { message = "نام برچسب حداکثر ۸۰ کاراکتر است" });

        var exists = await db.DocumentSystemTags.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Name == name, ct);
        if (exists) return Conflict(new { message = "این برچسب قبلا ثبت شده است" });

        var tag = new DocumentSystemTag
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentSystemTags.Add(tag);
        await db.SaveChangesAsync(ct);
        return Ok(new { tag.Id, tag.Name, message = "برچسب اضافه شد" });
    }

    [HttpDelete("settings/tags/{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> DeleteSystemTag(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var tag = await db.DocumentSystemTags.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tag is null) return NotFound(new { message = "برچسب یافت نشد" });
        tag.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "برچسب حذف شد" });
    }

    [HttpGet("settings/categories")]
    public async Task<IActionResult> GetSystemCategories([FromQuery] string? q, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        await EnsureDefaultCategoriesAsync(ct);
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 200);
        var categoriesQ = db.DocumentSystemCategories.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            categoriesQ = categoriesQ.Where(x => EF.Functions.Like(x.Name, like));
        }

        var items = await categoriesQ
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("settings/categories")]
    public async Task<IActionResult> CreateSystemCategory([FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام دسته‌بندی الزامی است" });
        if (name.Length > 80)
            return BadRequest(new { message = "نام دسته‌بندی حداکثر ۸۰ کاراکتر است" });

        var exists = await db.DocumentSystemCategories.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Name == name, ct);
        if (exists) return Conflict(new { message = "این دسته‌بندی قبلا ثبت شده است" });

        var category = new DocumentSystemCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentSystemCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return Ok(new { category.Id, category.Name, message = "دسته‌بندی اضافه شد" });
    }

    [HttpPatch("settings/categories/{id:guid}")]
    public async Task<IActionResult> UpdateSystemCategory(Guid id, [FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام دسته‌بندی الزامی است" });
        if (name.Length > 80)
            return BadRequest(new { message = "نام دسته‌بندی حداکثر ۸۰ کاراکتر است" });

        var category = await db.DocumentSystemCategories.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (category is null) return NotFound(new { message = "دسته‌بندی یافت نشد" });

        var exists = await db.DocumentSystemCategories.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Name == name && x.Id != id, ct);
        if (exists) return Conflict(new { message = "دسته‌بندی دیگری با این نام وجود دارد" });

        category.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(new { category.Id, category.Name, message = "دسته‌بندی بروزرسانی شد" });
    }

    [HttpDelete("settings/categories/{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> DeleteSystemCategory(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var category = await db.DocumentSystemCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return NotFound(new { message = "دسته‌بندی یافت نشد" });
        category.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "دسته‌بندی حذف شد" });
    }

    [HttpGet("settings/organizational-units")]
    public async Task<IActionResult> GetOrganizationalUnits([FromQuery] string? q, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 200);
        var query = db.DocumentSystemOrganizationalUnits.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            query = query.Where(x => EF.Functions.Like(x.Name, like));
        }

        var items = await query.OrderBy(x => x.Name).Take(take).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("settings/organizational-units")]
    public async Task<IActionResult> CreateOrganizationalUnit([FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام واحد سازمانی الزامی است" });
        if (name.Length > 120) return BadRequest(new { message = "نام واحد حداکثر ۱۲۰ کاراکتر است" });
        if (await db.DocumentSystemOrganizationalUnits.AsNoTracking().AnyAsync(x => x.IsActive && x.Name == name, ct))
            return Conflict(new { message = "این واحد سازمانی قبلا ثبت شده است" });

        var row = new DocumentSystemOrganizationalUnit
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentSystemOrganizationalUnits.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.Name, message = "واحد سازمانی اضافه شد" });
    }

    [HttpPatch("settings/organizational-units/{id:guid}")]
    public async Task<IActionResult> UpdateOrganizationalUnit(Guid id, [FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام واحد سازمانی الزامی است" });
        var row = await db.DocumentSystemOrganizationalUnits.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (row is null) return NotFound(new { message = "واحد سازمانی یافت نشد" });
        if (await db.DocumentSystemOrganizationalUnits.AsNoTracking().AnyAsync(x => x.IsActive && x.Name == name && x.Id != id, ct))
            return Conflict(new { message = "واحد دیگری با این نام وجود دارد" });
        row.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.Name, message = "واحد سازمانی بروزرسانی شد" });
    }

    [HttpDelete("settings/organizational-units/{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> DeleteOrganizationalUnit(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var row = await db.DocumentSystemOrganizationalUnits.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(new { message = "واحد سازمانی یافت نشد" });
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "واحد سازمانی حذف شد" });
    }

    [HttpGet("settings/projects")]
    public async Task<IActionResult> GetProjects([FromQuery] string? q, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 200);
        var query = db.DocumentSystemProjects.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var like = $"%{term}%";
            query = query.Where(x => EF.Functions.Like(x.Name, like));
        }

        var items = await query.OrderBy(x => x.Name).Take(take).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("settings/projects")]
    public async Task<IActionResult> CreateProject([FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام پروژه الزامی است" });
        if (name.Length > 120) return BadRequest(new { message = "نام پروژه حداکثر ۱۲۰ کاراکتر است" });
        if (await db.DocumentSystemProjects.AsNoTracking().AnyAsync(x => x.IsActive && x.Name == name, ct))
            return Conflict(new { message = "این پروژه قبلا ثبت شده است" });

        var row = new DocumentSystemProject
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentSystemProjects.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.Name, message = "پروژه اضافه شد" });
    }

    [HttpPatch("settings/projects/{id:guid}")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] CreateSystemTagRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام پروژه الزامی است" });
        var row = await db.DocumentSystemProjects.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (row is null) return NotFound(new { message = "پروژه یافت نشد" });
        if (await db.DocumentSystemProjects.AsNoTracking().AnyAsync(x => x.IsActive && x.Name == name && x.Id != id, ct))
            return Conflict(new { message = "پروژه دیگری با این نام وجود دارد" });
        row.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.Name, message = "پروژه بروزرسانی شد" });
    }

    [HttpDelete("settings/projects/{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var row = await db.DocumentSystemProjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(new { message = "پروژه یافت نشد" });
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پروژه حذف شد" });
    }

    [HttpPost("settings/reference-preview")]
    public IActionResult PreviewSystemReferences([FromBody] PreviewReferenceRequest? req)
    {
        var count = Math.Clamp(req?.Count ?? 1, 1, 100);
        var values = Enumerable.Range(0, count).Select(_ => GenerateSystemReferenceNumber()).ToList();
        return Ok(new { items = values });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] DocumentSearchRequest req, CancellationToken ct)
    {
        var query = (req.Query ?? "").Trim();
        var owners = (req.OwnerIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        var tags = (req.Tags ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        var types = (req.Types ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        var categoryFilter = (req.Category ?? "").Trim();

        var q = WhereVisibleInExplorer(db.Documents.AsNoTracking());
        if (req.CreatedStartUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= req.CreatedStartUtc.Value);
        if (req.CreatedEndUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc <= req.CreatedEndUtc.Value);
        if (owners.Count > 0)
            q = q.Where(x => owners.Contains(x.OwnerUserId));
        if (!string.IsNullOrWhiteSpace(req.Confidentiality))
        {
            var access = ParseAccessLevel(req.Confidentiality);
            q = q.Where(x => x.AccessLevel == access);
        }
        if (tags.Count > 0)
            q = q.Where(x => x.Tags.Any(t => tags.Contains(t.Tag)));
        if (types.Count > 0)
            q = q.Where(x => x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.Extension).Take(1).Any(ext => types.Contains(ext)));
        if (!string.IsNullOrWhiteSpace(categoryFilter))
            q = q.Where(x => x.Category == categoryFilter);
        if (req.OrganizationalUnitId.HasValue)
            q = q.Where(x => x.OrganizationalUnitId == req.OrganizationalUnitId.Value);
        if (req.ProjectId.HasValue)
            q = q.Where(x => x.ProjectId == req.ProjectId.Value);

        var rows = await q
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                organizationalUnitName = x.OrganizationalUnit != null ? x.OrganizationalUnit.Name : null,
                projectName = x.Project != null ? x.Project.Name : null,
                x.ReferenceNumber,
                x.ManualReferenceNumber,
                x.OwnerUserId,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                accessLevel = x.AccessLevel.ToString(),
                tags = x.Tags.Select(t => t.Tag).ToList(),
                latest = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new
                {
                    v.Extension,
                    v.SizeBytes,
                    v.ChangeLog,
                }).FirstOrDefault(),
            })
            .Take(1500)
            .ToListAsync(ct);

        var ownerIds = rows.Select(x => x.OwnerUserId).Distinct().ToList();
        var ownerMap = await db.Users.AsNoTracking()
            .Where(x => ownerIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var terms = ExtractSearchTerms(query);
        var matched = rows
            .Select(x =>
            {
                var ownerName = ownerMap.GetValueOrDefault(x.OwnerUserId, "—");
                if (string.IsNullOrWhiteSpace(ownerName)) ownerName = "—";
                var searchable = $"{x.Title} | {x.Category} | {x.ReferenceNumber} | {x.ManualReferenceNumber} | {string.Join(" ", x.tags)} | {ownerName} | {x.latest?.ChangeLog}";
                var isMatch = string.IsNullOrWhiteSpace(query) || EvaluateBooleanQuery(searchable, query);
                var relevance = CalculateRelevance(searchable, terms, x.Title);
                return new
                {
                    x.Id,
                    x.Title,
                    x.Category,
                    x.organizationalUnitName,
                    x.projectName,
                    x.ReferenceNumber,
                    x.ManualReferenceNumber,
                    owner = ownerName,
                    x.CreatedAtUtc,
                    x.UpdatedAtUtc,
                    x.accessLevel,
                    ext = x.latest?.Extension,
                    size = x.latest?.SizeBytes ?? 0,
                    tags = x.tags,
                    snippet = BuildSnippet(searchable, terms),
                    isMatch,
                    relevance,
                };
            })
            .Where(x => x.isMatch)
            .ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var (_, contentHits) = await contentSearch.SearchAsync(
                query, 0, 200, req.CreatedStartUtc, req.CreatedEndUtc, ct);
            var rowById = rows.ToDictionary(x => x.Id);
            var existingIds = matched.Select(x => x.Id).ToHashSet();
            foreach (var hit in contentHits)
            {
                if (existingIds.Contains(hit.DocumentId)) continue;
                if (!rowById.TryGetValue(hit.DocumentId, out var row)) continue;
                var ownerName = ownerMap.GetValueOrDefault(row.OwnerUserId, "—");
                if (string.IsNullOrWhiteSpace(ownerName)) ownerName = "—";
                matched.Add(new
                {
                    row.Id,
                    row.Title,
                    row.Category,
                    row.organizationalUnitName,
                    row.projectName,
                    row.ReferenceNumber,
                    row.ManualReferenceNumber,
                    owner = ownerName,
                    row.CreatedAtUtc,
                    row.UpdatedAtUtc,
                    row.accessLevel,
                    ext = row.latest?.Extension,
                    size = row.latest?.SizeBytes ?? 0,
                    tags = row.tags,
                    snippet = hit.Snippet,
                    isMatch = true,
                    relevance = (int)Math.Round(hit.Rank),
                });
                existingIds.Add(hit.DocumentId);
            }
        }

        var sort = (req.Sort ?? "relevance").Trim().ToLowerInvariant();
        matched = sort switch
        {
            "date_newest" => matched.OrderByDescending(x => x.CreatedAtUtc).ToList(),
            "date_oldest" => matched.OrderBy(x => x.CreatedAtUtc).ToList(),
            "size" => matched.OrderByDescending(x => x.size).ToList(),
            _ => matched.OrderByDescending(x => x.relevance).ThenByDescending(x => x.UpdatedAtUtc).ToList(),
        };

        return Ok(new
        {
            total = matched.Count,
            items = matched.Take(200).Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.ReferenceNumber,
                x.owner,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.accessLevel,
                x.ext,
                x.size,
                x.tags,
                x.snippet,
            }),
        });
    }

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام پوشه الزامی است" });
        if (name.Length > 200)
            return BadRequest(new { message = "نام پوشه حداکثر ۲۰۰ کاراکتر است" });

        var parentId = req.ParentId ?? await EnsureRootFolderAsync(ct);
        if (req.ParentId.HasValue)
        {
            var parentExists = await db.DocumentFolders.AsNoTracking().AnyAsync(x => x.Id == req.ParentId.Value, ct);
            if (!parentExists) return BadRequest(new { message = "پوشه والد نامعتبر است" });
        }

        var exists = await db.DocumentFolders.AsNoTracking()
            .AnyAsync(x => x.ParentId == parentId && x.Name == name, ct);
        if (exists)
            return Conflict(new { message = "پوشه‌ای با همین نام از قبل وجود دارد" });

        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (description?.Length > 500)
            return BadRequest(new { message = "توضیح پوشه حداکثر ۵۰۰ کاراکتر است" });

        var folder = new DocumentFolder
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            ParentId = parentId,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentFolders.Add(folder);
        await db.SaveChangesAsync(ct);
        return Ok(new { id = folder.Id, message = "پوشه ایجاد شد" });
    }

    [HttpPatch("folders/{id:guid}/rename")]
    public async Task<IActionResult> RenameFolder(Guid id, [FromBody] RenameFolderRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام جدید پوشه الزامی است" });
        if (name.Length > 200)
            return BadRequest(new { message = "نام پوشه حداکثر ۲۰۰ کاراکتر است" });

        var folder = await db.DocumentFolders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (folder is null) return NotFound(new { message = "پوشه یافت نشد" });
        if (folder.ParentId is null)
            return BadRequest(new { message = "تغییر نام پوشه ریشه مجاز نیست" });

        var exists = await db.DocumentFolders.AsNoTracking()
            .AnyAsync(x => x.ParentId == folder.ParentId && x.Name == name && x.Id != id, ct);
        if (exists)
            return Conflict(new { message = "پوشه‌ای با همین نام در این سطح وجود دارد" });

        folder.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "نام پوشه بروزرسانی شد" });
    }

    [HttpDelete("folders/{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        var folder = await db.DocumentFolders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (folder is null) return NotFound(new { message = "پوشه یافت نشد" });
        if (folder.ParentId is null)
            return BadRequest(new { message = "حذف پوشه ریشه مجاز نیست" });

        var hasChildren = await db.DocumentFolders.AsNoTracking().AnyAsync(x => x.ParentId == id, ct);
        if (hasChildren)
            return BadRequest(new { message = "پوشه دارای زیرپوشه است؛ ابتدا زیرپوشه‌ها را حذف کنید" });

        var hasDocuments = await db.Documents.AsNoTracking().AnyAsync(x => x.FolderId == id, ct);
        if (hasDocuments)
            return BadRequest(new { message = "پوشه دارای سند است؛ ابتدا اسناد داخل پوشه را حذف/انتقال دهید" });

        folder.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پوشه حذف شد" });
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentsForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (form.Files is null || form.Files.Count == 0)
            return BadRequest(new { message = "حداقل یک فایل انتخاب کنید" });

        var folderId = form.FolderId ?? await EnsureRootFolderAsync(ct);
        var folderExists = await db.DocumentFolders.AsNoTracking().AnyAsync(x => x.Id == folderId, ct);
        if (!folderExists) return BadRequest(new { message = "پوشه مقصد معتبر نیست" });

        var metadataItems = ParseMetadata(form.MetadataJson);
        var createdIds = new List<Guid>();
        var versionIdsToIndex = new List<Guid>();

        for (var i = 0; i < form.Files.Count; i++)
        {
            var file = form.Files[i];
            if (file.Length <= 0) continue;
            if (file.Length > MaxUploadBytes)
                return BadRequest(new { message = $"حجم فایل {file.FileName} بیشتر از 50MB است" });
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExt.Contains(ext))
                return BadRequest(new { message = $"پسوند فایل {file.FileName} مجاز نیست" });

            var md = i < metadataItems.Count ? metadataItems[i] : null;
            var systemReference = GenerateSystemReferenceNumber();
            var doc = new Document
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                Title = (md?.Title ?? Path.GetFileNameWithoutExtension(file.FileName)).Trim(),
                Category = string.IsNullOrWhiteSpace(md?.Category) ? "Correspondence" : md!.Category!.Trim(),
                DocumentDateUtc = TryParseDate(md?.DocumentDate),
                ReferenceNumber = systemReference,
                ManualReferenceNumber = string.IsNullOrWhiteSpace(md?.ManualReferenceNumber) ? null : md!.ManualReferenceNumber!.Trim(),
                Description = string.IsNullOrWhiteSpace(md?.Description) ? null : md!.Description!.Trim(),
                AccessLevel = ParseAccessLevel(md?.Confidentiality),
                OwnerUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            if (md?.OrganizationalUnitId is Guid ouId)
            {
                if (!await db.DocumentSystemOrganizationalUnits.AsNoTracking().AnyAsync(x => x.Id == ouId && x.IsActive, ct))
                    return BadRequest(new { message = $"واحد سازمانی نامعتبر برای فایل {file.FileName}" });
                doc.OrganizationalUnitId = ouId;
            }
            if (md?.ProjectId is Guid projId)
            {
                if (!await db.DocumentSystemProjects.AsNoTracking().AnyAsync(x => x.Id == projId && x.IsActive, ct))
                    return BadRequest(new { message = $"پروژه نامعتبر برای فایل {file.FileName}" });
                doc.ProjectId = projId;
            }
            db.Documents.Add(doc);

            var versionId = Guid.NewGuid();
            var contentHash = await DocumentTextIndexHelper.ComputeSha256HexAsync(file, ct);
            var saved = await files.SaveFromFormFileAsync(doc.Id, 1, file, ct);
            var version = new DocumentVersion
            {
                Id = versionId,
                DocumentId = doc.Id,
                VersionNumber = 1,
                OriginalFileName = saved.OriginalFileName,
                StoredPath = saved.RelativePath,
                Extension = saved.Extension,
                ContentHashSha256 = contentHash,
                ChangeLog = string.IsNullOrWhiteSpace(md?.ChangeLog) ? "نسخه اولیه" : md!.ChangeLog!.Trim(),
                UploadedByUserId = userId,
                UploadedAtUtc = DateTime.UtcNow,
            };
            DocumentVersionEncryptionMetadata.Apply(version, saved);
            db.DocumentVersions.Add(version);

            foreach (var tag in (md?.Tags ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct())
            {
                db.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = doc.Id,
                    Tag = tag,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }

            db.DocumentActivities.Add(CreateAudit(
                doc.Id,
                "Create",
                $"فایل «{file.FileName}» آپلود شد",
                userId,
                newValues: JsonSerializer.Serialize(new { file = file.FileName, version = 1, changeLog = md?.ChangeLog })));
            DocumentTextIndexHelper.AddPendingVersionText(db, doc.Id, versionId);
            createdIds.Add(doc.Id);
            versionIdsToIndex.Add(versionId);
        }

        await db.SaveChangesAsync(ct);
        foreach (var docId in createdIds)
        {
            var doc = await db.Documents.FirstAsync(x => x.Id == docId, ct);
            await lifecycleService.ApplyMatchingPolicyAsync(doc, ct);
        }
        if (createdIds.Count > 0)
            await db.SaveChangesAsync(ct);
        await DocumentTextIndexHelper.EnqueueAfterSaveAsync(textExtractionQueue, versionIdsToIndex, ct);
        return Ok(new { message = "آپلود اسناد با موفقیت انجام شد", ids = createdIds });
    }

    [HttpGet("{id:guid}/indexing-status")]
    public async Task<IActionResult> IndexingStatus(Guid id, CancellationToken ct)
    {
        var latest = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == id)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new { x.Id, x.VersionNumber, x.Extension })
            .FirstOrDefaultAsync(ct);
        if (latest is null) return NotFound(new { message = "سند یافت نشد" });

        var text = await db.DocumentVersionTexts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentVersionId == latest.Id, ct);

        return Ok(new
        {
            documentId = id,
            latest.VersionNumber,
            latest.Extension,
            status = text?.ProcessingStatus.ToString() ?? "Pending",
            charCount = text?.CharCount ?? 0,
            errorMessage = text?.ErrorMessage,
            processedAtUtc = text?.ProcessedAtUtc,
        });
    }

    [HttpGet("quick-search")]
    public async Task<IActionResult> QuickSearch(
        [FromQuery] string q,
        [FromQuery] int take = 12,
        CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        take = Math.Clamp(take, 1, 30);
        if (term.Length < 2)
            return Ok(new { folders = Array.Empty<object>(), files = Array.Empty<object>() });

        var like = $"%{term}%";

        var folders = await db.DocumentFolders.AsNoTracking()
            .Where(x => !x.IsDeleted && EF.Functions.Like(x.Name, like))
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new { id = x.Id, name = x.Name, parentId = x.ParentId })
            .ToListAsync(ct);

        var files = await WhereVisibleInExplorer(db.Documents.AsNoTracking())
            .Where(x =>
                EF.Functions.Like(x.Title, like)
                || EF.Functions.Like(x.ReferenceNumber ?? "", like)
                || EF.Functions.Like(x.ManualReferenceNumber ?? "", like)
                || x.Versions.Any(v => EF.Functions.Like(v.OriginalFileName, like)))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(take)
            .Select(x => new
            {
                id = x.Id,
                title = x.Title,
                folderId = x.FolderId,
                referenceNumber = x.ReferenceNumber,
                extension = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.Extension).FirstOrDefault(),
                originalFileName = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.OriginalFileName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(new { folders, files });
    }

    [HttpGet("content-search")]
    public async Task<IActionResult> ContentSearch(
        [FromQuery] string q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromQuery] DateTime? createdStartUtc = null,
        [FromQuery] DateTime? createdEndUtc = null,
        CancellationToken ct = default)
    {
        var (total, items) = await contentSearch.SearchAsync(q, skip, take, createdStartUtc, createdEndUtc, ct);
        return Ok(new
        {
            total,
            items = items.Select(x => new
            {
                x.DocumentId,
                x.Title,
                x.ReferenceNumber,
                x.VersionNumber,
                x.Extension,
                x.Rank,
                x.Snippet,
                processingStatus = x.ProcessingStatus,
            }),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        await ProcessDueScheduledWorkflowStartsAsync(ct);

        var doc = await db.Documents.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.FolderId,
                x.Title,
                x.Category,
                x.OrganizationalUnitId,
                organizationalUnitName = x.OrganizationalUnit != null ? x.OrganizationalUnit.Name : null,
                x.ProjectId,
                projectName = x.Project != null ? x.Project.Name : null,
                x.DocumentDateUtc,
                x.ReferenceNumber,
                x.ManualReferenceNumber,
                x.Description,
                accessLevel = x.AccessLevel.ToString(),
                x.OwnerUserId,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.WorkflowStatus,
                x.WorkflowTemplateId,
                x.WorkflowName,
                x.StepsJson,
                x.CurrentStepOrder,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                x.PostApprovalJson,
                x.WorkflowRunCycle,
                x.WorkflowRunsHistoryJson,
                x.WorkflowRejectionJson,
                x.ExpiresAtUtc,
                x.RetentionPolicyId,
                retentionPolicyName = x.RetentionPolicy != null ? x.RetentionPolicy.Name : null,
                archiveTier = x.ArchiveTier.ToString(),
                lifecycleStatus = x.LifecycleStatus.ToString(),
                x.IsArchived,
                x.ArchivedAtUtc,
                x.LegalHold,
                x.LegalHoldReason,
                x.LegalHoldStartedAtUtc,
                x.IsObsolete,
                x.ObsoleteAtUtc,
                x.ObsoleteReason,
                x.ScheduledArchiveAtUtc,
                x.ScheduledDeleteAtUtc,
                x.LongTermRetention,
                x.LifecycleWarningSentAtUtc,
                versions = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new
                {
                    v.Id,
                    v.VersionNumber,
                    v.OriginalFileName,
                    v.StoredPath,
                    v.Extension,
                    v.SizeBytes,
                    v.ChangeLog,
                    v.UploadedAtUtc,
                }).ToList(),
                tags = x.Tags.OrderBy(t => t.Tag).Select(t => t.Tag).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });

        var ownerName = "—";
        if (doc.OwnerUserId != Guid.Empty)
        {
            ownerName = await db.Users.AsNoTracking()
                .Where(x => x.Id == doc.OwnerUserId)
                .Select(x => (x.FirstName + " " + x.LastName).Trim())
                .FirstOrDefaultAsync(ct) ?? "—";
            if (string.IsNullOrWhiteSpace(ownerName)) ownerName = "—";
        }

        var activity = await db.DocumentActivities.AsNoTracking()
            .Where(a => a.DocumentId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(20)
            .Select(a => new { a.EventType, a.Message, a.CreatedAtUtc })
            .ToListAsync(ct);
        if (TryGetUserId(out var readerId))
        {
            db.DocumentActivities.Add(CreateAudit(id, "Read/View", "نمایش جزئیات سند", readerId));
            await db.SaveChangesAsync(ct);
        }

        var steps = DocumentWorkflowProcessor.DeserializeSteps(doc.StepsJson);
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserGuid);
        var isOwner = doc.OwnerUserId == currentUserGuid;
        var workflowDocument = new Document
        {
            Id = doc.Id,
            WorkflowStatus = doc.WorkflowStatus,
            WorkflowTemplateId = doc.WorkflowTemplateId,
            WorkflowName = doc.WorkflowName,
            StepsJson = doc.StepsJson,
            CurrentStepOrder = doc.CurrentStepOrder,
            WorkflowStartedAtUtc = doc.WorkflowStartedAtUtc,
            WorkflowScheduledStartAtUtc = doc.WorkflowScheduledStartAtUtc,
            WorkflowRejectionJson = doc.WorkflowRejectionJson,
            WorkflowRunCycle = doc.WorkflowRunCycle,
            OwnerUserId = doc.OwnerUserId,
        };
        var actionState = PostApprovalJsonHelper.DeserializeState(doc.PostApprovalJson);

        return Ok(new
        {
            doc.Id,
            folderId = doc.FolderId,
            doc.Title,
            doc.Category,
            organizationalUnitId = doc.OrganizationalUnitId,
            organizationalUnitName = doc.organizationalUnitName,
            projectId = doc.ProjectId,
            projectName = doc.projectName,
            doc.DocumentDateUtc,
            doc.ReferenceNumber,
            doc.ManualReferenceNumber,
            doc.Description,
            doc.accessLevel,
            owner = ownerName,
            doc.CreatedAtUtc,
            doc.UpdatedAtUtc,
            doc.versions,
            doc.tags,
            activity,
            workflowStatus = ToWorkflowClientStatus(doc.WorkflowStatus),
            doc.WorkflowName,
            doc.WorkflowTemplateId,
            doc.WorkflowStartedAtUtc,
            doc.WorkflowScheduledStartAtUtc,
            doc.CurrentStepOrder,
            doc.WorkflowRunCycle,
            CanAssignWorkflow = DocumentWorkflowAccessRules.CanAssignWorkflow(workflowDocument),
            CanUnassignWorkflow = DocumentWorkflowAccessRules.CanUnassignWorkflow(workflowDocument),
            CanStartWorkflow = DocumentWorkflowAccessRules.CanStartWorkflow(workflowDocument),
            WorkflowRejection = DocumentWorkflowRejectionHelper.BuildView(workflowDocument, isOwner),
            WorkflowRunsHistory = DocumentWorkflowRunHistoryHelper.Deserialize(doc.WorkflowRunsHistoryJson),
            HasActionPhase = actionState is { AssigneeUserIds.Count: > 0 },
            ActionDirectionLabel = actionState?.ActionDirectionLabel,
            ActionPhaseStatus = actionState?.Status,
            doc.ExpiresAtUtc,
            doc.RetentionPolicyId,
            doc.retentionPolicyName,
            doc.archiveTier,
            doc.lifecycleStatus,
            doc.IsArchived,
            doc.ArchivedAtUtc,
            doc.LegalHold,
            doc.LegalHoldReason,
            doc.LegalHoldStartedAtUtc,
            doc.IsObsolete,
            doc.ObsoleteAtUtc,
            doc.ObsoleteReason,
            doc.ScheduledArchiveAtUtc,
            doc.ScheduledDeleteAtUtc,
            doc.LongTermRetention,
            doc.LifecycleWarningSentAtUtc,
            CanArchive = !doc.IsArchived && !doc.LegalHold,
            CanRestoreFromArchive = doc.IsArchived,
            Steps = steps.Select(s => new
            {
                s.Order,
                s.UserId,
                s.UserName,
                s.Status,
                s.ActionAt,
                s.Note,
                s.Comment,
            }),
        });
    }

    [HttpPost("{id:guid}/assign-workflow")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> AssignWorkflow(Guid id, [FromBody] AssignWorkflowRequest req, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return NotFound(new { message = "سند یافت نشد" });
        if (!DocumentWorkflowAccessRules.CanAssignWorkflow(document))
            return BadRequest(new { message = BuildAssignWorkflowDeniedMessage(document) });

        if (!Guid.TryParse(req.WorkflowTemplateId, out var templateId))
            return BadRequest(new { message = "گردش انتخاب‌شده نامعتبر است" });

        var template = await db.DocumentWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct);
        if (template is null)
            return BadRequest(new { message = "گردش یافت نشد یا غیرفعال است" });

        var (ok, err, message) = await workflowAssignService.AssignAsync(document, template, req, User, ct);
        if (!ok) return BadRequest(new { message = err ?? "انتصاب گردش ناموفق بود" });

        return Ok(new
        {
            message,
            workflowStartedAtUtc = document.WorkflowStartedAtUtc,
            workflowScheduledStartAtUtc = document.WorkflowScheduledStartAtUtc,
            workflowRunCycle = document.WorkflowRunCycle,
            canStartWorkflow = DocumentWorkflowAccessRules.CanStartWorkflow(document),
        });
    }

    [HttpPost("{id:guid}/unassign-workflow")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> UnassignWorkflow(Guid id, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return NotFound(new { message = "سند یافت نشد" });
        if (!DocumentWorkflowAccessRules.CanUnassignWorkflow(document))
            return BadRequest(new { message = "در وضعیت فعلی امکان حذف گردش وجود ندارد" });

        document.WorkflowTemplateId = null;
        document.WorkflowName = null;
        document.WorkflowStartedAtUtc = null;
        document.WorkflowScheduledStartAtUtc = null;
        document.StepsJson = null;
        document.WorkflowStatus = DocumentWorkflowStatus.None;
        document.CurrentStepOrder = 0;
        document.WorkflowRejectionJson = null;
        document.PostApprovalJson = null;

        var links = await db.DocumentApprovalLinks
            .Where(x => x.DocumentId == id && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in links)
            link.IsActive = false;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گردش از سند حذف شد" });
    }

    [HttpPost("{id:guid}/start-workflow")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> StartWorkflow(Guid id, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return NotFound(new { message = "سند یافت نشد" });
        if (document.WorkflowTemplateId is null && string.IsNullOrWhiteSpace(document.StepsJson))
            return BadRequest(new { message = "برای این سند گردش تأیید تعریف نشده است" });
        if (document.WorkflowStartedAtUtc is not null)
            return BadRequest(new { message = "گردش این سند قبلاً شروع شده است" });
        if (document.WorkflowStatus != DocumentWorkflowStatus.Pending)
            return BadRequest(new { message = "گردش این سند قبلاً شروع شده یا به پایان رسیده است" });

        var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(document, ct);
        if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });

        return Ok(new { message = $"گردش «{document.WorkflowName ?? "تأیید"}» شروع شد" });
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var result = await workflowProcessor.ProcessActionAsync(id, userId, true, req.Comment, ct);
        if (!result.Success)
        {
            var code = result.Message?.Contains("امضا", StringComparison.Ordinal) == true
                ? "signature_required"
                : "workflow_action_failed";
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code });
        }
        return Ok(new { message = result.Message });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var result = await workflowProcessor.ProcessActionAsync(id, userId, false, req.Comment, ct);
        if (!result.Success)
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code = "workflow_action_failed" });
        return Ok(new { message = result.Message });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] Guid? versionId,
        [FromQuery] bool inline = false,
        CancellationToken ct = default)
    {
        var docExists = await db.Documents.AsNoTracking().AnyAsync(x => x.Id == id, ct);
        if (!docExists) return NotFound(new { message = "سند یافت نشد" });

        var q = db.DocumentVersions.AsNoTracking().Where(x => x.DocumentId == id);
        var version = versionId.HasValue
            ? await q.FirstOrDefaultAsync(x => x.Id == versionId.Value, ct)
            : await q.OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct);
        if (version is null) return NotFound(new { message = "نسخه فایل یافت نشد" });

        if (TryGetUserId(out var downloaderId))
        {
            var action = inline ? "Read/View" : "Download";
            var message = inline ? "پیش‌نمایش سند" : "دانلود سند";
            db.DocumentActivities.Add(CreateAudit(id, action, message, downloaderId, reason: $"versionId={version.Id}"));
            await db.SaveChangesAsync(ct);
        }

        var contentType = GetDocumentContentType(version.Extension, version.OriginalFileName);
        var downloadName = string.IsNullOrWhiteSpace(version.OriginalFileName)
            ? $"document-v{version.VersionNumber}"
            : version.OriginalFileName;

        var served = await DocumentVersionFileHttpHelper.TryServePhysicalAsync(
            files, version, contentType, downloadName, inline, Response, ct);
        if (served is null)
            return NotFound(new { message = "فایل فیزیکی موجود نیست" });
        return served;
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid id,
        [FromQuery] Guid? versionId,
        CancellationToken ct = default)
    {
        var docExists = await db.Documents.AsNoTracking().AnyAsync(x => x.Id == id, ct);
        if (!docExists) return NotFound(new { message = "سند یافت نشد" });

        var q = db.DocumentVersions.AsNoTracking().Where(x => x.DocumentId == id);
        var version = versionId.HasValue
            ? await q.FirstOrDefaultAsync(x => x.Id == versionId.Value, ct)
            : await q.OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct);
        if (version is null) return NotFound(new { message = "نسخه فایل یافت نشد" });

        if (!files.FileExists(version))
            return NotFound(new { message = "فایل فیزیکی موجود نیست" });

        if (TryGetUserId(out var viewerId))
        {
            db.DocumentActivities.Add(CreateAudit(id, "Read/View", "پیش‌نمایش سند", viewerId, reason: $"versionId={version.Id}"));
            await db.SaveChangesAsync(ct);
        }

        await using var localFile = await files.OpenLocalPathAsync(version, ct);
        var fullPath = localFile.Path;

        var ext = NormalizeFileExtension(version.Extension, version.OriginalFileName);
        if (ext == ".zip")
            return BadRequest(new { message = "پیش‌نمایش فایل فشرده پشتیبانی نمی‌شود" });

        var previewName = string.IsNullOrWhiteSpace(version.OriginalFileName)
            ? $"document-v{version.VersionNumber}"
            : Path.GetFileNameWithoutExtension(version.OriginalFileName);

        if (ext is ".jpg" or ".jpeg" or ".png")
        {
            var imageName = string.IsNullOrWhiteSpace(version.OriginalFileName)
                ? $"document-v{version.VersionNumber}{ext}"
                : version.OriginalFileName;
            ContentDispositionHelper.SetInline(Response, imageName);
            return PhysicalFile(fullPath, GetDocumentContentType(version.Extension, version.OriginalFileName));
        }

        if (ext == ".pdf")
        {
            var pdfName = string.IsNullOrWhiteSpace(version.OriginalFileName)
                ? $"document-v{version.VersionNumber}.pdf"
                : version.OriginalFileName;
            ContentDispositionHelper.SetInline(Response, pdfName);
            Response.Headers["X-Preview-Extension"] = "pdf";
            return PhysicalFile(fullPath, "application/pdf");
        }

        if (ext is ".doc" or ".docx" or ".xlsx")
        {
            if (!libreOffice.IsAvailable)
            {
                return StatusCode(503, new
                {
                    message = "LibreOffice روی سرور نصب نیست. برای پیش‌نمایش Word/Excel، LibreOffice را نصب و API را ری‌استارت کنید.",
                });
            }

            var convertedPdf = libreOffice.TryConvertToPdf(fullPath);
            if (convertedPdf is null)
                return StatusCode(500, new { message = "تبدیل فایل به PDF برای پیش‌نمایش ناموفق بود" });

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(convertedPdf, ct);
            try { System.IO.File.Delete(convertedPdf); } catch { /* temp cleanup */ }

            Response.Headers["X-Preview-Source"] = "libreoffice-pdf";
            Response.Headers["X-Preview-Extension"] = ext.TrimStart('.');
            ContentDispositionHelper.SetInline(Response, $"{previewName}.pdf");
            return new FileContentResult(pdfBytes, "application/pdf");
        }

        return BadRequest(new { message = "پیش‌نمایش این نوع فایل پشتیبانی نمی‌شود" });
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> Versions(Guid id, CancellationToken ct)
    {
        var exists = await db.Documents.AsNoTracking().AnyAsync(x => x.Id == id, ct);
        if (!exists) return NotFound(new { message = "سند یافت نشد" });

        var rows = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == id)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new
            {
                x.Id,
                x.VersionNumber,
                x.OriginalFileName,
                x.Extension,
                x.SizeBytes,
                x.ChangeLog,
                x.UploadedByUserId,
                x.UploadedAtUtc,
            })
            .ToListAsync(ct);
        var userIds = rows.Select(x => x.UploadedByUserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = (x.FirstName + " " + x.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var currentVersion = rows.Select(x => x.VersionNumber).DefaultIfEmpty(0).Max();

        return Ok(new
        {
            currentVersion,
            items = rows.Select((x, idx) => new
            {
                x.Id,
                x.VersionNumber,
                x.OriginalFileName,
                x.Extension,
                x.SizeBytes,
                x.ChangeLog,
                uploadedBy = users.GetValueOrDefault(x.UploadedByUserId, "کاربر"),
                x.UploadedAtUtc,
                isCurrent = x.VersionNumber == currentVersion,
                sizeDeltaBytes = idx + 1 < rows.Count ? x.SizeBytes - rows[idx + 1].SizeBytes : 0L,
            }),
        });
    }

    [HttpPost("{id:guid}/versions")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadVersion(Guid id, [FromForm] UploadDocumentVersionForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (form.File is null || form.File.Length <= 0) return BadRequest(new { message = "فایل نسخه الزامی است" });
        if (form.File.Length > MaxUploadBytes) return BadRequest(new { message = "حجم فایل بیشتر از 50MB است" });
        var ext = Path.GetExtension(form.File.FileName);
        if (!AllowedExt.Contains(ext)) return BadRequest(new { message = "پسوند فایل مجاز نیست" });

        var currentMax = await db.DocumentVersions
            .Where(x => x.DocumentId == id)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0;
        var nextVersion = currentMax + 1;

        var versionId = Guid.NewGuid();
        var contentHash = await DocumentTextIndexHelper.ComputeSha256HexAsync(form.File, ct);
        var saved = await files.SaveFromFormFileAsync(doc.Id, nextVersion, form.File, ct);
        var newVersion = new DocumentVersion
        {
            Id = versionId,
            DocumentId = doc.Id,
            VersionNumber = nextVersion,
            OriginalFileName = saved.OriginalFileName,
            StoredPath = saved.RelativePath,
            Extension = saved.Extension,
            ContentHashSha256 = contentHash,
            ChangeLog = string.IsNullOrWhiteSpace(form.ChangeLog) ? null : form.ChangeLog.Trim(),
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow,
        };
        DocumentVersionEncryptionMetadata.Apply(newVersion, saved);
        db.DocumentVersions.Add(newVersion);
        doc.UpdatedAtUtc = DateTime.UtcNow;
        DocumentTextIndexHelper.AddPendingVersionText(db, doc.Id, versionId);
        db.DocumentActivities.Add(CreateAudit(
            doc.Id,
            "Update",
            $"نسخه v{nextVersion} ثبت شد",
            userId,
            newValues: JsonSerializer.Serialize(new { version = nextVersion, file = form.File.FileName, changeLog = form.ChangeLog })));
        await db.SaveChangesAsync(ct);
        await textExtractionQueue.EnqueueAsync(versionId, ct);
        return Ok(new { message = "نسخه جدید ثبت شد", versionNumber = nextVersion });
    }

    [HttpPost("{id:guid}/versions/compare")]
    public async Task<IActionResult> CompareVersions(Guid id, [FromBody] CompareVersionsRequest req, CancellationToken ct)
    {
        if (req.NewVersionId == Guid.Empty || req.OldVersionId == Guid.Empty)
            return BadRequest(new { message = "شناسه نسخه‌ها الزامی است" });

        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == id && (x.Id == req.NewVersionId || x.Id == req.OldVersionId))
            .ToListAsync(ct);
        if (versions.Count != 2)
            return NotFound(new { message = "یکی از نسخه‌ها یافت نشد" });

        var newer = versions.First(x => x.Id == req.NewVersionId);
        var older = versions.First(x => x.Id == req.OldVersionId);

        if (!files.FileExists(newer) || !files.FileExists(older))
            return NotFound(new { message = "فایل یکی از نسخه‌ها موجود نیست" });

        await using var newerLocal = await files.OpenLocalPathAsync(newer, ct);
        await using var olderLocal = await files.OpenLocalPathAsync(older, ct);
        var newPath = newerLocal.Path;
        var oldPath = olderLocal.Path;

        if (!IsDocxExtension(older.Extension) || !IsDocxExtension(newer.Extension))
        {
            var newText = ExtractVersionPlainText(newPath);
            var oldText = ExtractVersionPlainText(oldPath);
            if (newText is null || oldText is null)
            {
                return BadRequest(new
                {
                    message = libreOffice.IsAvailable
                        ? "استخراج متن برای مقایسه ناموفق بود"
                        : "برای مقایسه Word/Excel/PDF، LibreOffice را روی سرور API نصب کنید",
                    libreOfficeAvailable = libreOffice.IsAvailable,
                });
            }

            return Ok(new
            {
                oldLabel = $"v{older.VersionNumber}",
                newLabel = $"v{newer.VersionNumber}",
                oldText,
                newText,
                libreOfficeAvailable = libreOffice.IsAvailable,
            });
        }

        try
        {
            var oldText = DocxTextExtractor.ExtractPlainText(oldPath);
            var newText = DocxTextExtractor.ExtractPlainText(newPath);
            return Ok(new
            {
                oldLabel = $"v{older.VersionNumber}",
                newLabel = $"v{newer.VersionNumber}",
                oldText,
                newText,
                libreOfficeAvailable = libreOffice.IsAvailable,
                extractor = "openxml",
            });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "خواندن محتوای Word برای مقایسه ناموفق بود" });
        }
    }

    [HttpGet("{id:guid}/compare/{v1:int}/{v2:int}")]
    public async Task<IActionResult> CompareWordByVersionNumbers(Guid id, int v1, int v2, CancellationToken ct)
    {
        if (v1 <= 0 || v2 <= 0 || v1 == v2)
            return BadRequest(new { message = "شماره نسخه‌ها باید مثبت و متفاوت باشند" });

        var docExists = await db.Documents.AsNoTracking().AnyAsync(x => x.Id == id, ct);
        if (!docExists) return NotFound(new { message = "سند یافت نشد" });

        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == id && (x.VersionNumber == v1 || x.VersionNumber == v2))
            .ToListAsync(ct);
        if (versions.Count != 2)
            return NotFound(new { message = "یکی از نسخه‌های درخواستی یافت نشد" });

        var first = versions.First(x => x.VersionNumber == v1);
        var second = versions.First(x => x.VersionNumber == v2);
        var older = v1 < v2 ? first : second;
        var newer = v1 < v2 ? second : first;

        if (!IsDocxExtension(older.Extension) || !IsDocxExtension(newer.Extension))
            return BadRequest(new { message = "مقایسه OpenXml فقط برای فایل Word (docx) پشتیبانی می‌شود" });

        if (!files.FileExists(older) || !files.FileExists(newer))
            return NotFound(new { message = "فایل یکی از نسخه‌ها موجود نیست" });

        await using var olderLocal2 = await files.OpenLocalPathAsync(older, ct);
        await using var newerLocal2 = await files.OpenLocalPathAsync(newer, ct);

        try
        {
            var oldText = DocxTextExtractor.ExtractPlainText(olderLocal2.Path);
            var newText = DocxTextExtractor.ExtractPlainText(newerLocal2.Path);
            return Ok(new
            {
                documentId = id,
                oldVersion = older.VersionNumber,
                newVersion = newer.VersionNumber,
                oldText,
                newText,
            });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "خواندن محتوای Word ناموفق بود" });
        }
    }

    [HttpGet("libreoffice-status")]
    public IActionResult LibreOfficeStatus() =>
        Ok(new { available = libreOffice.IsAvailable });

    [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, Guid versionId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        var source = await db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentId == id && x.Id == versionId, ct);
        if (source is null) return NotFound(new { message = "نسخه موردنظر یافت نشد" });
        if (!files.FileExists(source))
            return NotFound(new { message = "فایل نسخه انتخاب‌شده موجود نیست" });

        var currentMax = await db.DocumentVersions
            .Where(x => x.DocumentId == id)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0;
        var nextVersion = currentMax + 1;

        await using var sourceLocal = await files.OpenLocalPathAsync(source, ct);
        await using var src = System.IO.File.OpenRead(sourceLocal.Path);
        var saved = await files.SaveFromStreamAsync(doc.Id, nextVersion, src, source.OriginalFileName, ct);
        var restoredVersion = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            VersionNumber = nextVersion,
            OriginalFileName = source.OriginalFileName,
            StoredPath = saved.RelativePath,
            Extension = source.Extension,
            ChangeLog = $"Restore از نسخه v{source.VersionNumber}",
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow,
        };
        DocumentVersionEncryptionMetadata.Apply(restoredVersion, saved);
        db.DocumentVersions.Add(restoredVersion);
        doc.UpdatedAtUtc = DateTime.UtcNow;
        db.DocumentActivities.Add(CreateAudit(
            doc.Id,
            "Rollback",
            $"نسخه v{source.VersionNumber} بازیابی شد و v{nextVersion} ساخته شد",
            userId,
            oldValues: JsonSerializer.Serialize(new { restoredFromVersion = source.VersionNumber }),
            newValues: JsonSerializer.Serialize(new { newCurrentVersion = nextVersion })));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "بازیابی نسخه انجام شد", newVersion = nextVersion });
    }

    [HttpPatch("{id:guid}/metadata")]
    public async Task<IActionResult> UpdateMetadata(Guid id, [FromBody] UpdateDocumentMetadataRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var doc = await db.Documents
            .Include(x => x.Tags)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });

        if (req.Category is not null)
        {
            var category = req.Category.Trim();
            if (string.IsNullOrWhiteSpace(category))
                return BadRequest(new { message = "دسته‌بندی موضوعی نمی‌تواند خالی باشد" });
            doc.Category = category;
        }

        if (req.ApplyClassification)
        {
            if (req.OrganizationalUnitId is Guid ouId)
            {
                if (!await db.DocumentSystemOrganizationalUnits.AsNoTracking().AnyAsync(x => x.Id == ouId && x.IsActive, ct))
                    return BadRequest(new { message = "واحد سازمانی نامعتبر است" });
                doc.OrganizationalUnitId = ouId;
            }
            else
                doc.OrganizationalUnitId = null;

            if (req.ProjectId is Guid projId)
            {
                if (!await db.DocumentSystemProjects.AsNoTracking().AnyAsync(x => x.Id == projId && x.IsActive, ct))
                    return BadRequest(new { message = "پروژه نامعتبر است" });
                doc.ProjectId = projId;
            }
            else
                doc.ProjectId = null;
        }

        if (req.Description is not null)
            doc.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.ManualReferenceNumber is not null)
            doc.ManualReferenceNumber = string.IsNullOrWhiteSpace(req.ManualReferenceNumber) ? null : req.ManualReferenceNumber.Trim();
        if (req.DocumentDate is not null)
            doc.DocumentDateUtc = TryParseDate(req.DocumentDate);
        if (!string.IsNullOrWhiteSpace(req.Confidentiality))
            doc.AccessLevel = ParseAccessLevel(req.Confidentiality);

        if (req.Tags is not null)
        {
            var newTags = req.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            db.DocumentTags.RemoveRange(doc.Tags);
            foreach (var tag in newTags)
            {
                db.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = doc.Id,
                    Tag = tag,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
        }

        doc.UpdatedAtUtc = DateTime.UtcNow;
        db.DocumentActivities.Add(CreateAudit(doc.Id, "Update", "متادیتا و طبقه‌بندی سند بروزرسانی شد", userId));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "اطلاعات سند ذخیره شد" });
    }

    [HttpPatch("{id:guid}/rename")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameDocumentRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var title = (req.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان جدید الزامی است" });

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        var oldTitle = doc.Title;
        doc.Title = title;
        doc.UpdatedAtUtc = DateTime.UtcNow;
        db.DocumentActivities.Add(CreateAudit(
            doc.Id,
            "Update",
            $"عنوان سند به «{title}» تغییر یافت",
            userId,
            oldValues: JsonSerializer.Serialize(new { title = oldTitle }),
            newValues: JsonSerializer.Serialize(new { title })));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "عنوان سند بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "forms.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (doc.LegalHold)
            return BadRequest(new { message = "سند در Legal Hold است و قابل حذف نیست" });
        doc.IsDeleted = true;
        doc.UpdatedAtUtc = DateTime.UtcNow;
        db.DocumentActivities.Add(CreateAudit(doc.Id, "Delete", "سند حذف شد", userId));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سند حذف شد" });
    }

    private async Task EnsureDefaultCategoriesAsync(CancellationToken ct)
    {
        if (await db.DocumentSystemCategories.AsNoTracking().AnyAsync(x => x.IsActive, ct))
            return;

        var defaults = new[] { "مکاتبات", "قرارداد", "فاکتور", "نقشه فنی" };
        foreach (var name in defaults)
        {
            db.DocumentSystemCategories.Add(new DocumentSystemCategory
            {
                Id = Guid.NewGuid(),
                Name = name,
                IsActive = true,
                CreatedByUserId = Guid.Empty,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid> EnsureRootFolderAsync(CancellationToken ct)
    {
        var root = await db.DocumentFolders.FirstOrDefaultAsync(x => x.ParentId == null && x.Name == "Root", ct);
        if (root is not null) return root.Id;

        root = new DocumentFolder
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            ParentId = null,
            CreatedByUserId = Guid.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentFolders.Add(root);
        await db.SaveChangesAsync(ct);
        return root.Id;
    }

    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt.ToUniversalTime();
        return null;
    }

    private string? ExtractVersionPlainText(string fullPath)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!IsComparableOfficeExt(ext))
            return null;

        return libreOffice.TryExtractPlainText(fullPath);
    }

    private static bool IsDocxExtension(string? extension) =>
        string.Equals((extension ?? "").Trim().TrimStart('.'), "docx", StringComparison.OrdinalIgnoreCase);

    private static bool IsComparableOfficeExt(string ext) =>
        ext is ".pdf" or ".docx" or ".xlsx" or ".txt";

    private static string NormalizeFileExtension(string? extension, string? originalFileName)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) && !string.IsNullOrWhiteSpace(originalFileName))
            ext = Path.GetExtension(originalFileName);
        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith('.'))
            ext = "." + ext.TrimStart('.');
        return ext;
    }

    private static string GetDocumentContentType(string? extension, string? originalFileName)
    {
        var ext = NormalizeFileExtension(extension, originalFileName).TrimStart('.');

        return ext switch
        {
            "pdf" => "application/pdf",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }

    private static DocumentAccessLevel ParseAccessLevel(string? level)
    {
        return (level ?? "").Trim().ToLowerInvariant() switch
        {
            "public" => DocumentAccessLevel.Public,
            "confidential" => DocumentAccessLevel.Confidential,
            "highly secret" => DocumentAccessLevel.HighlySecret,
            "highlysecret" => DocumentAccessLevel.HighlySecret,
            _ => DocumentAccessLevel.Internal,
        };
    }

    private bool TryGetUserId(out Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out id);
    }

    private static List<UploadMetadataItem> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<UploadMetadataItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>اسناد فعال برای کاوشگر و جستجوی اصلی — بدون بایگانی و منسوخ.</summary>
    private static IQueryable<Document> WhereVisibleInExplorer(IQueryable<Document> query) =>
        query.Where(x =>
            x.LifecycleStatus == DocumentLifecycleStatus.Active
            && !x.IsArchived
            && !x.IsObsolete);

    private static string GenerateSystemReferenceNumber()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3.5));
        Span<byte> bytes = stackalloc byte[3];
        RandomNumberGenerator.Fill(bytes);
        var suffix = BitConverter.ToString(bytes.ToArray()).Replace("-", "");
        return $"SYS-{now:yyyyMMddHHmmss}-{suffix}";
    }

    private static List<string> ExtractSearchTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return Regex.Split(query, @"\s+")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x =>
            {
                var u = x.Trim().ToUpperInvariant();
                return u != "AND" && u != "OR" && u != "NOT";
            })
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool EvaluateBooleanQuery(string text, string query)
    {
        var tokens = Regex.Split(query, @"\s+").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (tokens.Count == 0) return true;
        bool has = false;
        bool result = false;
        string currentOp = "AND";

        for (var i = 0; i < tokens.Count; i++)
        {
            var tk = tokens[i].Trim();
            var up = tk.ToUpperInvariant();
            if (up == "AND" || up == "OR")
            {
                currentOp = up;
                continue;
            }
            var negate = false;
            if (up == "NOT")
            {
                negate = true;
                i++;
                if (i >= tokens.Count) break;
                tk = tokens[i].Trim();
            }

            var termMatch = text.Contains(tk, StringComparison.OrdinalIgnoreCase);
            var value = negate ? !termMatch : termMatch;
            if (!has)
            {
                result = value;
                has = true;
            }
            else
            {
                result = currentOp == "OR" ? (result || value) : (result && value);
            }
        }
        return has && result;
    }

    private static int CalculateRelevance(string text, List<string> terms, string title)
    {
        if (terms.Count == 0) return 0;
        var score = 0;
        foreach (var t in terms)
        {
            if (title.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 8;
            var idx = 0;
            while (true)
            {
                idx = text.IndexOf(t, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                score += 2;
                idx += t.Length;
            }
        }
        return score;
    }

    private static string BuildSnippet(string text, List<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (terms.Count == 0) return text.Length <= 180 ? text : text[..180] + "...";
        var first = terms
            .Select(t => new { t, idx = text.IndexOf(t, StringComparison.OrdinalIgnoreCase) })
            .Where(x => x.idx >= 0)
            .OrderBy(x => x.idx)
            .FirstOrDefault();
        if (first is null) return text.Length <= 180 ? text : text[..180] + "...";
        var start = Math.Max(0, first.idx - 60);
        var len = Math.Min(180, text.Length - start);
        var snippet = text.Substring(start, len);
        if (start > 0) snippet = "..." + snippet;
        if (start + len < text.Length) snippet += "...";
        return snippet;
    }

    private async Task ProcessDueScheduledWorkflowStartsAsync(CancellationToken ct)
    {
        var due = await db.Documents
            .Where(x => x.WorkflowScheduledStartAtUtc != null
                && x.WorkflowScheduledStartAtUtc <= DateTime.UtcNow
                && x.WorkflowStartedAtUtc == null
                && x.WorkflowTemplateId != null
                && x.WorkflowStatus == DocumentWorkflowStatus.Pending)
            .ToListAsync(ct);
        foreach (var document in due)
            await workflowProcessor.TryStartWorkflowAsync(document, ct);
    }

    private static string BuildAssignWorkflowDeniedMessage(Document document)
    {
        if (document.WorkflowStatus == DocumentWorkflowStatus.InProgress)
            return "این پرونده در حال گردش است؛ تا پایان گردش فعلی امکان اتصال گردش جدید وجود ندارد";
        if (DocumentWorkflowAccessRules.HasAssignedWorkflow(document) && document.WorkflowStartedAtUtc is null)
            return "گردش قبلاً انتصاب شده است؛ ابتدا آن را شروع کنید یا لغو کنید";
        if (DocumentWorkflowAccessRules.HasWorkflowActivity(document) && document.WorkflowStatus != DocumentWorkflowStatus.Rejected)
            return "در وضعیت فعلی امکان انتصاب گردش وجود ندارد";
        return "در وضعیت فعلی امکان انتصاب گردش وجود ندارد";
    }

    private static string ToWorkflowClientStatus(DocumentWorkflowStatus status) => status switch
    {
        DocumentWorkflowStatus.Pending => "pending",
        DocumentWorkflowStatus.InProgress => "in_progress",
        DocumentWorkflowStatus.Approved => "approved",
        DocumentWorkflowStatus.Rejected => "rejected",
        _ => "none",
    };

    private DocumentActivity CreateAudit(
        Guid documentId,
        string action,
        string message,
        Guid? actorUserId,
        string? reason = null,
        string? oldValues = null,
        string? newValues = null)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 500) userAgent = userAgent[..500];
        return new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            EventType = action,
            Message = message,
            ActorUserId = actorUserId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = userAgent,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            OldValuesJson = oldValues,
            NewValuesJson = newValues,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }
}

public sealed class CreateFolderRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
}

public sealed class RenameFolderRequest
{
    public string? Name { get; set; }
}

public sealed class CreateSystemTagRequest
{
    public string? Name { get; set; }
}

public sealed class PreviewReferenceRequest
{
    public int Count { get; set; } = 1;
}

public sealed class RenameDocumentRequest
{
    public string? Title { get; set; }
}

public sealed class UploadDocumentsForm
{
    public Guid? FolderId { get; set; }
    public string? MetadataJson { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}

public sealed class UpdateDocumentMetadataRequest
{
    public bool ApplyClassification { get; set; }
    public string? Category { get; set; }
    public Guid? OrganizationalUnitId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Description { get; set; }
    public string? ManualReferenceNumber { get; set; }
    public string? DocumentDate { get; set; }
    public string? Confidentiality { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class UploadMetadataItem
{
    public string? Title { get; set; }
    public string? Category { get; set; }
    public Guid? OrganizationalUnitId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? DocumentDate { get; set; }
    public string? ManualReferenceNumber { get; set; }
    public string? Description { get; set; }
    public string? Confidentiality { get; set; }
    public string? ChangeLog { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class UploadDocumentVersionForm
{
    public IFormFile? File { get; set; }
    public string? ChangeLog { get; set; }
}

public sealed class CompareVersionsRequest
{
    public Guid NewVersionId { get; set; }
    public Guid OldVersionId { get; set; }
}

public sealed class DocumentSearchRequest
{
    public string? Query { get; set; }
    public DateTime? CreatedStartUtc { get; set; }
    public DateTime? CreatedEndUtc { get; set; }
    public List<string>? Types { get; set; }
    public List<Guid>? OwnerIds { get; set; }
    public string? Confidentiality { get; set; }
    public string? Category { get; set; }
    public Guid? OrganizationalUnitId { get; set; }
    public Guid? ProjectId { get; set; }
    public List<string>? Tags { get; set; }
    public string? Sort { get; set; }
}
