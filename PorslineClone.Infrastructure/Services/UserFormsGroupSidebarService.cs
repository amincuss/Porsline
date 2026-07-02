using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>
/// آمار sidebar صفحه فرم‌های کاربران — هر گروه جداگانه و فقط بر اساس داده‌های همان گروه.
/// </summary>
public class UserFormsGroupSidebarService(AppDbContext db)
{
    public record GroupSidebarItemDto(
        Guid Id,
        string Name,
        Guid? PrimaryFormId,
        string? PrimaryFormTitle,
        int SubmissionCount,
        int DispatchedCount,
        int PendingCount,
        int MemberCount,
        int RegisteredMemberCount,
        int NotRegisteredMemberCount,
        int DuplicateResponderCount,
        int DuplicateSubmissionCount);

    private sealed record GroupActivityRow(
        Guid GroupId,
        Guid FormId,
        Guid ResponderId,
        DateTime LinkCreatedAtUtc,
        Guid LinkId);

    private sealed record GroupSubmissionRow(
        Guid GroupId,
        Guid FormId,
        Guid ResponderId);

    public async Task<IReadOnlyList<GroupSidebarItemDto>> BuildAsync(CancellationToken ct = default)
    {
        var groups = await db.ResponderGroups.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        if (groups.Count == 0)
            return [];

        var snapshot = await LoadSnapshotForActiveGroupsAsync(ct);

        return groups
            .Select(g => BuildItem(g.Id, g.Name, snapshot, formId: null))
            .Where(x => x.MemberCount > 0 || x.SubmissionCount > 0 || x.DispatchedCount > 0)
            .ToList();
    }

    public async Task<Guid?> ResolvePrimaryFormIdAsync(Guid groupId, CancellationToken ct = default)
    {
        var snapshot = await LoadSnapshotForGroupAsync(groupId, ct);
        return ResolvePrimaryFormId(groupId, snapshot);
    }

    public async Task<GroupSidebarItemDto?> GetGroupStatsAsync(
        Guid groupId,
        Guid? formId = null,
        CancellationToken ct = default)
    {
        var group = await db.ResponderGroups.AsNoTracking()
            .Where(x => x.Id == groupId && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(ct);
        if (group is null)
            return null;

        var snapshot = await LoadSnapshotForGroupAsync(groupId, ct);
        return BuildItem(group.Id, group.Name, snapshot, formId);
    }

    private static GroupSidebarItemDto BuildItem(
        Guid groupId,
        string groupName,
        GroupSnapshot snapshot,
        Guid? formId)
    {
        var memberCount = snapshot.MemberCountByGroup.GetValueOrDefault(groupId);
        var primaryFormId = formId is Guid explicitId && explicitId != Guid.Empty
            ? explicitId
            : ResolvePrimaryFormId(groupId, snapshot);

        if (primaryFormId is not Guid fid || fid == Guid.Empty)
        {
            return new GroupSidebarItemDto(
                groupId,
                groupName,
                null,
                null,
                0,
                0,
                0,
                memberCount,
                0,
                memberCount,
                0,
                0);
        }

        snapshot.FormTitles.TryGetValue(fid, out var formTitle);

        var submissionCount = snapshot.Submissions.Count(x => x.GroupId == groupId && x.FormId == fid);
        var submittedResponders = snapshot.Submissions
            .Where(x => x.GroupId == groupId && x.FormId == fid)
            .Select(x => x.ResponderId)
            .ToHashSet();

        var manuallyRemovedResponders = snapshot.DeletedSubmissions
            .Where(x => x.GroupId == groupId && x.FormId == fid)
            .Select(x => x.ResponderId)
            .ToHashSet();

        var registeredMemberCount = submittedResponders.Count;
        var notRegisteredMemberCount = Math.Max(
            0,
            memberCount - registeredMemberCount - manuallyRemovedResponders.Count);

        var activeDispatched = snapshot.Links
            .Where(x => x.GroupId == groupId && x.FormId == fid)
            .GroupBy(x => x.ResponderId)
            .Select(g => g.OrderByDescending(x => x.LinkCreatedAtUtc).First())
            .Where(x => !manuallyRemovedResponders.Contains(x.ResponderId))
            .ToList();

        var dispatchedCount = activeDispatched.Count;
        var pendingCount = activeDispatched.Count(x => !submittedResponders.Contains(x.ResponderId));

        var duplicateGroups = snapshot.Submissions
            .Where(x => x.GroupId == groupId && x.FormId == fid)
            .GroupBy(x => x.ResponderId)
            .Where(g => g.Count() > 1)
            .ToList();
        var duplicateResponderCount = duplicateGroups.Count;
        var duplicateSubmissionCount = duplicateGroups.Sum(g => g.Count() - 1);

        return new GroupSidebarItemDto(
            groupId,
            groupName,
            fid,
            formTitle,
            submissionCount,
            dispatchedCount,
            pendingCount,
            memberCount,
            registeredMemberCount,
            notRegisteredMemberCount,
            duplicateResponderCount,
            duplicateSubmissionCount);
    }

    private static Guid? ResolvePrimaryFormId(Guid groupId, GroupSnapshot snapshot)
    {
        if (snapshot.LatestJobFormByGroup.TryGetValue(groupId, out var fromJob) && fromJob != Guid.Empty)
            return fromJob;

        var groupLinks = snapshot.Links.Where(x => x.GroupId == groupId).ToList();
        if (groupLinks.Count > 0)
        {
            return groupLinks
                .GroupBy(x => x.FormId)
                .OrderByDescending(g => g.Max(x => x.LinkCreatedAtUtc))
                .ThenByDescending(g => g.Select(x => x.ResponderId).Distinct().Count())
                .Select(g => (Guid?)g.Key)
                .FirstOrDefault();
        }

        var groupSubs = snapshot.Submissions.Where(x => x.GroupId == groupId).ToList();
        if (groupSubs.Count > 0)
        {
            return groupSubs
                .GroupBy(x => x.FormId)
                .OrderByDescending(g => g.Count())
                .Select(g => (Guid?)g.Key)
                .FirstOrDefault();
        }

        return null;
    }

    private Task<GroupSnapshot> LoadSnapshotForActiveGroupsAsync(CancellationToken ct) =>
        LoadSnapshotCoreAsync(onlyGroupId: null, ct);

    private Task<GroupSnapshot> LoadSnapshotForGroupAsync(Guid groupId, CancellationToken ct) =>
        LoadSnapshotCoreAsync(groupId, ct);

    private async Task<GroupSnapshot> LoadSnapshotCoreAsync(Guid? onlyGroupId, CancellationToken ct)
    {
        var memberCounts = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive
            where onlyGroupId == null || m.GroupId == onlyGroupId
            group m by m.GroupId into grp
            select new { GroupId = grp.Key, Count = grp.Count() }
        ).ToListAsync(ct);

        var latestJobs = await (
            from j in db.FormDispatchGroupSendJobs.AsNoTracking()
            join g in db.ResponderGroups.AsNoTracking() on j.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive && j.FormId != Guid.Empty
            where onlyGroupId == null || j.GroupId == onlyGroupId
            orderby j.CreatedAtUtc descending
            select new { j.GroupId, j.FormId, j.CreatedAtUtc }
        ).ToListAsync(ct);

        var latestJobFormByGroup = latestJobs
            .GroupBy(j => j.GroupId)
            .ToDictionary(g => g.Key, g => g.First().FormId);

        var links = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive
            where onlyGroupId == null || m.GroupId == onlyGroupId
            join l in db.FormDispatchLinks.AsNoTracking() on m.ResponderId equals l.ResponderId
            join f in db.Forms.AsNoTracking() on l.FormId equals f.Id
            where !f.IsDeleted
            select new GroupActivityRow(m.GroupId, l.FormId, m.ResponderId, l.CreatedAtUtc, l.Id)
        ).ToListAsync(ct);

        var submissions = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive
            where onlyGroupId == null || m.GroupId == onlyGroupId
            join s in db.FormSubmissions.AsNoTracking() on m.ResponderId equals s.ResponderId
            join f in db.Forms.AsNoTracking() on s.FormId equals f.Id
            where !f.IsDeleted
            select new GroupSubmissionRow(m.GroupId, s.FormId, m.ResponderId)
        ).ToListAsync(ct);

        // ثبت‌های حذف‌شده دستی از لیست — در آمار sidebar شمرده نمی‌شوند
        var deletedSubmissions = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive
            where onlyGroupId == null || m.GroupId == onlyGroupId
            join s in db.FormSubmissions.IgnoreQueryFilters().AsNoTracking() on m.ResponderId equals s.ResponderId
            where s.IsDeleted
            join f in db.Forms.AsNoTracking() on s.FormId equals f.Id
            where !f.IsDeleted
            select new GroupSubmissionRow(m.GroupId, s.FormId, m.ResponderId)
        ).ToListAsync(ct);

        var formTitles = await db.Forms.AsNoTracking()
            .Where(f => !f.IsDeleted)
            .ToDictionaryAsync(f => f.Id, f => f.Title, ct);

        return new GroupSnapshot(
            memberCounts.ToDictionary(x => x.GroupId, x => x.Count),
            latestJobFormByGroup,
            links,
            submissions,
            deletedSubmissions,
            formTitles);
    }

    private sealed record GroupSnapshot(
        Dictionary<Guid, int> MemberCountByGroup,
        Dictionary<Guid, Guid> LatestJobFormByGroup,
        List<GroupActivityRow> Links,
        List<GroupSubmissionRow> Submissions,
        List<GroupSubmissionRow> DeletedSubmissions,
        Dictionary<Guid, string> FormTitles)
    {
        public static GroupSnapshot Empty { get; } = new([], [], [], [], [], []);
    }
}
