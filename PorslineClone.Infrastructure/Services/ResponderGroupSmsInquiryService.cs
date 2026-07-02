using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public record ResponderGroupSmsInquiryItemDto(
    Guid ResponderId,
    string FullName,
    string MobileNumber,
    Guid? SmsLogId,
    bool? IsSuccess,
    string? ErrorMessage,
    DateTime? SentAtUtc,
    string? FormTitle,
    Guid? FormId,
    bool HasSubmittedForm,
    string? TrackingCode,
    DateTime? SubmittedAtUtc);

public record ResponderGroupSmsInquiryResponseDto(
    Guid GroupId,
    string GroupName,
    int TotalMembers,
    int PendingFormCount,
    int SuccessCount,
    int FailedCount,
    int NoLogCount,
    int CompletedFormCount,
    List<ResponderGroupSmsInquiryItemDto> Items);

public record ResponderGroupSmsPendingSummaryDto(
    int PendingFormCount,
    int TotalMembers,
    Guid? PrimaryFormId,
    string? PrimaryFormTitle,
    int RegisteredCount = 0);

public record GroupFormRegistrationStatsDto(
    Guid FormId,
    string FormTitle,
    int RegisteredCount,
    int DispatchedCount,
    int PendingCount);

public class ResponderGroupSmsInquiryService(AppDbContext db, UserFormsGroupSidebarService sidebar)
{
    public async Task<ResponderGroupSmsPendingSummaryDto?> GetPendingSummaryAsync(
        Guid groupId,
        Guid? formId = null,
        CancellationToken ct = default)
    {
        var groupExists = await db.ResponderGroups.AsNoTracking()
            .AnyAsync(x => x.Id == groupId && !x.IsDeleted, ct);
        if (!groupExists) return null;

        var stats = await GetGroupFormRegistrationStatsAsync(groupId, formId, ct);
        if (stats is null)
            return new ResponderGroupSmsPendingSummaryDto(0, 0, null, null, 0);

        return new ResponderGroupSmsPendingSummaryDto(
            stats.PendingCount,
            stats.DispatchedCount,
            stats.FormId,
            stats.FormTitle,
            stats.RegisteredCount);
    }

    public async Task<GroupFormRegistrationStatsDto?> GetGroupFormRegistrationStatsAsync(
        Guid groupId,
        Guid? formId = null,
        CancellationToken ct = default)
    {
        var stats = await sidebar.GetGroupStatsAsync(groupId, formId, ct);
        if (stats?.PrimaryFormId is not Guid fid || fid == Guid.Empty)
            return null;

        return new GroupFormRegistrationStatsDto(
            fid,
            stats.PrimaryFormTitle ?? "",
            stats.SubmissionCount,
            stats.DispatchedCount,
            stats.PendingCount);
    }

    public async Task<Dictionary<Guid, GroupFormRegistrationStatsDto>> GetGroupFormRegistrationStatsBatchAsync(
        IReadOnlyList<Guid> groupIds,
        CancellationToken ct = default)
    {
        if (groupIds.Count == 0)
            return new Dictionary<Guid, GroupFormRegistrationStatsDto>();

        var items = await sidebar.BuildAsync(ct);
        return items
            .Where(x => groupIds.Contains(x.Id) && x.PrimaryFormId is Guid)
            .ToDictionary(
                x => x.Id,
                x => new GroupFormRegistrationStatsDto(
                    x.PrimaryFormId!.Value,
                    x.PrimaryFormTitle ?? "",
                    x.SubmissionCount,
                    x.DispatchedCount,
                    x.PendingCount));
    }

    public async Task<ResponderGroupSmsInquiryResponseDto?> GetAsync(
        Guid groupId,
        Guid? formId = null,
        bool onlyIncomplete = false,
        CancellationToken ct = default)
    {
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(120);
        try
        {
            return await GetAsyncCore(groupId, formId, onlyIncomplete, ct);
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task<ResponderGroupSmsInquiryResponseDto?> GetAsyncCore(
        Guid groupId,
        Guid? formId,
        bool onlyIncomplete,
        CancellationToken ct)
    {
        var group = await db.ResponderGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && !x.IsDeleted, ct);
        if (group is null) return null;

        var context = await BuildFormDispatchContextAsync(groupId, formId, ct);
        if (context is null)
        {
            return new ResponderGroupSmsInquiryResponseDto(
                group.Id,
                group.Name,
                0,
                0,
                0,
                0,
                0,
                0,
                []);
        }

        var (registeredCount, pendingFormCount, dispatchedCount) = ComputeDispatchStats(context);

        var dispatchLogs = await db.SmsLogs.AsNoTracking()
            .Where(x => x.Source != null && x.Source.StartsWith("form.dispatch"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10000)
            .ToListAsync(ct);

        var mobileKeys = context.DispatchedMembers
            .Select(m => FormSubmissionMobileHelper.NormalizeMobile(m.MobileNumber))
            .Where(m => m.Length >= 10)
            .ToHashSet(StringComparer.Ordinal);

        var latestLogByMobile = new Dictionary<string, Domain.Entities.SmsLog>(StringComparer.Ordinal);
        foreach (var log in dispatchLogs)
        {
            var key = FormSubmissionMobileHelper.NormalizeMobile(log.MobileNumber);
            if (key.Length < 10 || !mobileKeys.Contains(key)) continue;
            if (!latestLogByMobile.ContainsKey(key))
                latestLogByMobile[key] = log;
        }

        var items = new List<ResponderGroupSmsInquiryItemDto>();
        var successCount = 0;
        var failedCount = 0;
        var noLogCount = 0;

        foreach (var member in context.DispatchedMembers)
        {
            var mobileKey = FormSubmissionMobileHelper.NormalizeMobile(member.MobileNumber);
            latestLogByMobile.TryGetValue(mobileKey, out var log);

            if (log is null)
            {
                noLogCount++;
                items.Add(new ResponderGroupSmsInquiryItemDto(
                    member.ResponderId,
                    member.FullName,
                    member.MobileNumber,
                    null,
                    null,
                    null,
                    null,
                    context.FormTitle,
                    context.FormId,
                    member.HasSubmitted,
                    member.TrackingCode,
                    member.SubmittedAtUtc));
                continue;
            }

            if (log.IsSuccess) successCount++;
            else failedCount++;

            items.Add(new ResponderGroupSmsInquiryItemDto(
                member.ResponderId,
                member.FullName,
                member.MobileNumber,
                log.Id,
                log.IsSuccess,
                log.ErrorMessage,
                log.CreatedAtUtc,
                context.FormTitle,
                context.FormId,
                member.HasSubmitted,
                member.TrackingCode,
                member.SubmittedAtUtc));
        }

        var dispatchedCountFinal = dispatchedCount;
        var pendingFormCountFinal = pendingFormCount;

        if (onlyIncomplete)
        {
            items = items.Where(x => !x.HasSubmittedForm).ToList();
            successCount = items.Count(x => x.IsSuccess == true);
            failedCount = items.Count(x => x.IsSuccess == false);
            noLogCount = items.Count(x => x.IsSuccess == null);
        }

        return new ResponderGroupSmsInquiryResponseDto(
            group.Id,
            group.Name,
            dispatchedCountFinal,
            pendingFormCountFinal,
            successCount,
            failedCount,
            noLogCount,
            registeredCount,
            items);
    }

    private static (int Registered, int Pending, int Dispatched) ComputeDispatchStats(FormDispatchContext context)
    {
        var dispatched = context.DispatchedMembers.Count;
        var registered = context.DispatchedMembers.Count(m => m.HasSubmitted);
        return (registered, dispatched - registered, dispatched);
    }

    /// <summary>
    /// فقط پاسخگوهایی که برای فرم مشخص لینک dispatch دریافت کرده‌اند.
    /// </summary>
    private async Task<FormDispatchContext?> BuildFormDispatchContextAsync(
        Guid groupId,
        Guid? formId,
        CancellationToken ct)
    {
        var effectiveFormId = formId is Guid ff && ff != Guid.Empty
            ? ff
            : await sidebar.ResolvePrimaryFormIdAsync(groupId, ct);

        if (effectiveFormId is not Guid resolvedFormId || resolvedFormId == Guid.Empty)
            return null;

        var formTitle = await db.Forms.AsNoTracking()
            .Where(f => f.Id == resolvedFormId && !f.IsDeleted)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(formTitle))
            return null;

        // join به‌جای Contains روی لیست Guid — از OPENJSON/WITH که روی برخی SQL Serverها خطا می‌دهد جلوگیری می‌کند
        var linkRows = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join r in db.Responders.AsNoTracking() on m.ResponderId equals r.Id
            join l in db.FormDispatchLinks.AsNoTracking() on m.ResponderId equals l.ResponderId
            where l.FormId == resolvedFormId
            select new
            {
                m.ResponderId,
                r.FullName,
                r.MobileNumber,
                LinkId = l.Id,
                l.CreatedAtUtc,
            }
        ).ToListAsync(ct);

        if (linkRows.Count == 0)
            return new FormDispatchContext(resolvedFormId, formTitle, []);

        var latestLinkByResponder = linkRows
            .GroupBy(x => x.ResponderId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CreatedAtUtc).First());

        var submissionRows = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join l in db.FormDispatchLinks.AsNoTracking() on m.ResponderId equals l.ResponderId
            where l.FormId == resolvedFormId
            join s in db.FormSubmissions.AsNoTracking() on l.Id equals s.DispatchLinkId
            select new { LinkId = l.Id, s.TrackingCode, s.SubmittedAtUtc }
        ).ToListAsync(ct);

        var submissionByLinkId = submissionRows
            .GroupBy(x => x.LinkId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var s = g.OrderByDescending(x => x.SubmittedAtUtc).First();
                    return new SubmissionInfo(s.TrackingCode, s.SubmittedAtUtc);
                });

        var dispatchedMembers = latestLinkByResponder
            .Select(pair =>
            {
                var row = pair.Value;
                var linkId = row.LinkId;
                var hasSubmitted = submissionByLinkId.ContainsKey(linkId);
                submissionByLinkId.TryGetValue(linkId, out var submission);
                return new DispatchedMemberRow(
                    pair.Key,
                    row.FullName ?? "",
                    row.MobileNumber ?? "",
                    linkId,
                    hasSubmitted,
                    submission?.TrackingCode,
                    submission?.SubmittedAtUtc);
            })
            .OrderBy(x => x.FullName)
            .ToList();

        return new FormDispatchContext(resolvedFormId, formTitle, dispatchedMembers);
    }

    /// <summary>اولویت با فرمی که بیشترین ثبت‌نام در گروه را دارد (صفحه فرم کاربران).</summary>
    public Task<Guid?> GetPrimaryFormIdForGroupAsync(Guid groupId, CancellationToken ct = default)
        => sidebar.ResolvePrimaryFormIdAsync(groupId, ct);

    public async Task<Guid?> ResolveEffectiveFormIdForGroupFilterAsync(
        Guid? groupId,
        bool ungroupedOnly,
        Guid? formId,
        CancellationToken ct = default)
    {
        if (ungroupedOnly || groupId is null || groupId == Guid.Empty)
            return null;

        var resolved = await GetPrimaryFormIdForGroupAsync(groupId.Value, ct);
        if (resolved is Guid fromServer && fromServer != Guid.Empty)
            return fromServer;

        if (formId is Guid explicitId && explicitId != Guid.Empty)
            return explicitId;

        return null;
    }

    private sealed record FormDispatchContext(
        Guid FormId,
        string FormTitle,
        List<DispatchedMemberRow> DispatchedMembers);

    private sealed record DispatchedMemberRow(
        Guid ResponderId,
        string FullName,
        string MobileNumber,
        Guid LinkId,
        bool HasSubmitted,
        string? TrackingCode,
        DateTime? SubmittedAtUtc);

    private sealed record SubmissionInfo(string? TrackingCode, DateTime SubmittedAtUtc);
}
