using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.SmsPatterns;

public class SmsPatternService(AppDbContext db, IMemoryCache cache) : ISmsPatternService
{
    private const string CacheKey = "sms-patterns-all";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

  private const string LegacyDispatchDefaultTemplate =
        "سلام {fullName}\nفرم «{formTitle}» برای شما ارسال شد.\nلطفا از لینک زیر تکمیل کنید:\n{link}";

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existingRows = await db.SmsPatterns.ToListAsync(ct);
        var existingSet = existingRows.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var seed in SmsPatternDefaults.All)
        {
            if (existingSet.Contains(seed.Key)) continue;
            db.SmsPatterns.Add(SmsPatternDefaults.ToEntity(seed));
            changed = true;
        }

        foreach (var row in existingRows)
        {
            var seed = SmsPatternDefaults.Find(row.Key);
            if (seed is null) continue;
            if (!NeedsPlaceholderSync(row.PlaceholdersJson, seed.Placeholders))
                continue;

            row.PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders, JsonOpts);
            if (!string.IsNullOrWhiteSpace(seed.Description))
                row.Description = seed.Description;
            if (string.Equals(row.Key, "form.dispatch.link.default", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Template.Trim(), LegacyDispatchDefaultTemplate, StringComparison.Ordinal))
                row.Template = seed.Template;
            row.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
            cache.Remove(CacheKey);
        }
    }

    public async Task<IReadOnlyList<SmsPatternCategoryDto>> GetGroupedAsync(CancellationToken ct = default)
    {
        var rows = await db.SmsPatterns.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var meta = SmsPatternDefaults.CategoryMeta.GetValueOrDefault(g.Key);
                return new SmsPatternCategoryDto(
                    g.Key,
                    meta.Title ?? g.Key,
                    meta.Icon ?? "MessageSquare",
                    meta.Color ?? "#8B5CF6",
                    g.Select(MapDto).ToList());
            })
            .ToList();
    }

    public async Task<string> RenderAsync(
        string key,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(key, ct);
        var result = template;
        foreach (var kv in values)
            result = result.Replace($"{{{kv.Key}}}", kv.Value ?? "", StringComparison.Ordinal);
        return result;
    }

    public async Task ResetToDefaultAsync(string key, CancellationToken ct = default)
    {
        var seed = SmsPatternDefaults.Find(key)
            ?? throw new InvalidOperationException("پترن پیش‌فرض یافت نشد");

        var row = await db.SmsPatterns.FirstOrDefaultAsync(x => x.Key == key, ct)
            ?? throw new InvalidOperationException("پترن در دیتابیس یافت نشد");

        row.Template = seed.Template;
        row.PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders, JsonOpts);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    public async Task UpdateTemplatesAsync(IReadOnlyList<UpdateSmsPatternItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        var keys = items.Select(x => x.Key).ToList();
        var rows = await db.SmsPatterns.Where(x => keys.Contains(x.Key)).ToListAsync(ct);
        var byKey = rows.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!byKey.TryGetValue(item.Key, out var row)) continue;
            if (string.IsNullOrWhiteSpace(item.Template)) continue;
            row.Template = item.Template.Trim();
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    private async Task<string> GetTemplateAsync(string key, CancellationToken ct)
    {
        var cached = await GetCachedTemplatesAsync(ct);
        if (cached.TryGetValue(key, out var template) && !string.IsNullOrWhiteSpace(template))
            return template;

        var fallback = SmsPatternDefaults.GetTemplate(key);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        throw new InvalidOperationException($"SmsPattern not found: {key}");
    }

    private async Task<Dictionary<string, string>> GetCachedTemplatesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<string, string>? hit) && hit is not null)
            return hit;

        var rows = await db.SmsPatterns.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Key, x.Template })
            .ToListAsync(ct);

        var dict = rows.ToDictionary(x => x.Key, x => x.Template, StringComparer.OrdinalIgnoreCase);
        cache.Set(CacheKey, dict, CacheDuration);
        return dict;
    }

    private static bool NeedsPlaceholderSync(
        string placeholdersJson,
        IReadOnlyList<SmsPatternPlaceholderDto> defaultPlaceholders)
    {
        var stored = DeserializePlaceholders(placeholdersJson);
        var storedKeys = stored.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return defaultPlaceholders.Any(d => !storedKeys.Contains(d.Key));
    }

    private static List<SmsPatternPlaceholderDto> DeserializePlaceholders(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SmsPatternPlaceholderDto>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<SmsPatternPlaceholderDto> ResolvePlaceholders(SmsPattern row)
    {
        var stored = DeserializePlaceholders(row.PlaceholdersJson);
        var seed = SmsPatternDefaults.Find(row.Key);
        if (seed is null || seed.Placeholders.Length == 0)
            return stored;

        var byKey = stored.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        return seed.Placeholders
            .Select(d => byKey.TryGetValue(d.Key, out var s)
                ? new SmsPatternPlaceholderDto(d.Key, d.Label, d.Sample ?? s.Sample)
                : d)
            .ToList();
    }

    private static SmsPatternDto MapDto(SmsPattern row)
    {
        var placeholders = ResolvePlaceholders(row);

        return new SmsPatternDto(
            row.Id,
            row.Key,
            row.Title,
            row.Category,
            row.Icon,
            row.IconColor,
            row.Template,
            placeholders,
            row.Description,
            row.SortOrder,
            row.IsActive,
            row.UpdatedAtUtc);
    }
}
