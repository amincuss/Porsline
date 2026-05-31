using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>تکمیل نام، سمت، امضا و آواتار مراحل گردش سند برای نمایش در API.</summary>
public static class DocumentWorkflowStepEnricher
{
    public static async Task<IReadOnlyDictionary<Guid, string?>> EnrichAsync(
        AppDbContext db,
        Guid documentId,
        List<ApprovalStepDto> steps,
        string signatureUrlTemplate,
        CancellationToken ct = default)
    {
        var approverIds = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (approverIds.Count == 0)
            return new Dictionary<Guid, string?>();

        var approvers = await db.Users.AsNoTracking()
            .Where(u => approverIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Gender,
                u.AvatarUrl,
                u.SignatureImagePath,
                u.SignatureDisplayDegree,
                PositionTitle = u.UserPosition != null ? u.UserPosition.Name : null,
            })
            .ToListAsync(ct);

        var avatarPaths = approvers.ToDictionary(u => u.Id, u => u.AvatarUrl);
        var userSigs = approvers.ToDictionary(
            u => u.Id,
            u => (u.SignatureImagePath, u.SignatureDisplayDegree));

        foreach (var step in steps)
        {
            var profile = approvers.FirstOrDefault(u => u.Id == step.UserId);
            if (profile is null) continue;
            FormApprovalSignatureHelper.EnrichApproverIdentityFromProfile(
                step, profile.FirstName, profile.LastName, profile.PositionTitle, profile.Gender);
        }

        FormApprovalSignatureHelper.BackfillApprovedStepSignatures(steps, userSigs);
        FormApprovalSignatureHelper.EnrichSignatureUrls(steps, s =>
            string.Format(signatureUrlTemplate, documentId, s.Order));

        return avatarPaths;
    }

    public static string? BuildAvatarUrl(
        string contentRoot,
        Guid userId,
        IReadOnlyDictionary<Guid, string?> avatarPaths) =>
        userId == Guid.Empty
            ? null
            : ProfileAvatarUrlHelper.BuildPublicUrl(
                contentRoot,
                userId,
                avatarPaths.TryGetValue(userId, out var av) ? av : null);
}
