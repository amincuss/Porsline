using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class ResponderGroupSubmissionFilter
{
    public static IQueryable<FormSubmission> Apply(
        AppDbContext db,
        IQueryable<FormSubmission> q,
        Guid? groupId,
        bool ungroupedOnly,
        Guid? formId = null)
    {
        if (ungroupedOnly)
        {
            var inAnyGroup = db.ResponderGroupMembers.Select(m => m.ResponderId);
            return q.Where(x =>
                x.ResponderId == null || !inAnyGroup.Contains(x.ResponderId.Value));
        }

        if (groupId is { } gid && gid != Guid.Empty)
        {
            var memberIds = db.ResponderGroupMembers
                .Where(m => m.GroupId == gid)
                .Select(m => m.ResponderId);
            q = q.Where(x => x.ResponderId != null && memberIds.Contains(x.ResponderId.Value));

            if (formId is Guid fid && fid != Guid.Empty)
                q = q.Where(x => x.FormId == fid);
        }

        return q;
    }
}
