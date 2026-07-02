using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Sms;

public sealed class SmsTestService(
    SmsGatewayDiagnostics diagnostics,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    AppDbContext db)
{
    public const string Source = "settings.sms.test";

    public SmsGatewayStatusDto GetGatewayStatus() => diagnostics.GetStatus();

    public async Task<IReadOnlyList<SmsTestPatternOptionDto>> GetPatternOptionsAsync(CancellationToken ct = default)
    {
        await smsPatterns.EnsureSeededAsync(ct);
        var groups = await smsPatterns.GetGroupedAsync(ct);
        return groups
            .SelectMany(g => g.Patterns.Where(p => p.IsActive))
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SmsTestPatternOptionDto(p.Key, p.Title, p.Category, p.Placeholders))
            .ToList();
    }

    public async Task<SmsTestPreviewResponse> PreviewAsync(SmsTestPreviewRequest request, CancellationToken ct = default)
    {
        var message = await ResolveMessageAsync(request.PatternKey, request.Message, request.PatternVars, ct);
        var mode = string.IsNullOrWhiteSpace(request.PatternKey) ? "manual" : "pattern";
        return new SmsTestPreviewResponse(message, mode);
    }

    public async Task<SmsTestSendResponse> SendAsync(SmsTestSendRequest request, CancellationToken ct = default)
    {
        var status = diagnostics.GetStatus();
        if (!status.IsConfigured)
        {
            return new SmsTestSendResponse(
                false,
                "درگاه پیامک پیکربندی نشده است",
                string.Join("؛ ", status.ConfigurationIssues),
                "",
                null,
                null);
        }

        var mobile = UserFieldNormalizer.NormalizeMobile(request.MobileNumber);
        if (!UserFieldNormalizer.IsValidMobile(mobile))
        {
            return new SmsTestSendResponse(
                false,
                "شماره موبایل معتبر نیست",
                "شماره باید با 09 شروع شود و ۱۱ رقم باشد",
                "",
                null,
                null);
        }

        string rendered;
        try
        {
            rendered = await ResolveMessageAsync(request.PatternKey, request.Message, request.PatternVars, ct);
        }
        catch (InvalidOperationException ex)
        {
            return new SmsTestSendResponse(false, ex.Message, ex.Message, "", null, null);
        }

        if (string.IsNullOrWhiteSpace(rendered))
        {
            return new SmsTestSendResponse(
                false,
                "متن پیامک خالی است",
                "متن دستی یا پترن را وارد کنید",
                "",
                null,
                null);
        }

        var sentAtUtc = DateTime.UtcNow;
        var ok = await smsSender.SendSmsAsync(new SmsRequest(mobile, rendered, Source), ct);

        var log = await db.SmsLogs.AsNoTracking()
            .Where(x => x.MobileNumber == mobile && x.Source == Source && x.CreatedAtUtc >= sentAtUtc.AddSeconds(-5))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return new SmsTestSendResponse(
            ok,
            ok ? "پیامک تست با موفقیت ارسال شد" : "ارسال پیامک تست ناموفق بود",
            log?.ErrorMessage,
            rendered,
            log?.Id,
            log?.HttpStatusCode);
    }

    private async Task<string> ResolveMessageAsync(
        string? patternKey,
        string? manualMessage,
        Dictionary<string, string?>? patternVars,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(patternKey))
        {
            await smsPatterns.EnsureSeededAsync(ct);
            var options = await GetPatternOptionsAsync(ct);
            var pattern = options.FirstOrDefault(p =>
                string.Equals(p.Key, patternKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (pattern is null)
                throw new InvalidOperationException("پترن پیامک یافت نشد");

            var values = BuildPatternValues(pattern, patternVars);
            return (await smsPatterns.RenderAsync(pattern.Key, values, ct)).Trim();
        }

        return (manualMessage ?? "").Trim();
    }

    private static Dictionary<string, string?> BuildPatternValues(
        SmsTestPatternOptionDto pattern,
        Dictionary<string, string?>? input)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var placeholder in pattern.Placeholders)
        {
            if (input is not null
                && input.TryGetValue(placeholder.Key, out var raw)
                && !string.IsNullOrWhiteSpace(raw))
            {
                values[placeholder.Key] = raw.Trim();
                continue;
            }

            values[placeholder.Key] = string.IsNullOrWhiteSpace(placeholder.Sample)
                ? $"[{placeholder.Label}]"
                : placeholder.Sample;
        }

        return values;
    }
}
