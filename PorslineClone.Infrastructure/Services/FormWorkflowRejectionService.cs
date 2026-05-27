using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormWorkflowRejectionService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    FormSubmissionApprovalLinkService approvalLinks,
    FormDispatchSubmissionNotifier dispatchNotifier)
{
    public async Task<bool> IsDispatchSenderAsync(FormSubmission submission, Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin) return true;
        if (submission.DispatchLinkId is not Guid linkId) return false;
        return await db.FormDispatchLinks.AsNoTracking()
            .AnyAsync(l => l.Id == linkId && l.SentByUserId == userId, ct);
    }

    public async Task<(bool Ok, string? Error)> RequestReapprovalAsync(
        FormSubmission submission,
        Guid actorUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        if (!FormWorkflowRejectionHelper.IsAwaitingSender(submission))
            return (false, "درخواست مجدد تأیید در این مرحله مجاز نیست");

        if (!await IsDispatchSenderAsync(submission, actorUserId, isAdmin, ct))
            return (false, "فقط ارسال‌کننده لینک فرم می‌تواند درخواست مجدد تأیید ثبت کند");

        var state = FormWorkflowRejectionHelper.Deserialize(submission.WorkflowRejectionJson)!;
        var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);
        var rejecterStep = steps.FirstOrDefault(s => s.Order == state.RejectedAtStepOrder);
        if (rejecterStep is null)
            return (false, "مرحله ردکننده یافت نشد");

        rejecterStep.Status = "pending";
        rejecterStep.ReviewCycle = Math.Max(0, rejecterStep.ReviewCycle) + 1;
        rejecterStep.LastRejectionComment = state.RejectionComment;
        rejecterStep.LastRejectedAtUtc = state.RejectedAtUtc;

        foreach (var s in steps.Where(x => x.Order != rejecterStep.Order))
        {
            if (s.Status == "pending") s.Status = "waiting";
        }

        state.Phase = "awaiting_reapprover";
        submission.WorkflowRejectionJson = FormWorkflowRejectionHelper.Serialize(state);
        submission.Status = FormSubmissionStatus.InProgress;
        submission.CurrentStepOrder = rejecterStep.Order;
        submission.StepsJson = WorkflowStepJsonHelper.Serialize(steps);

        await db.SaveChangesAsync(ct);

        var rejecterName = rejecterStep.UserName;
        if (string.IsNullOrWhiteSpace(rejecterName))
        {
            var u = await userManager.FindByIdAsync(rejecterStep.UserId.ToString());
            rejecterName = u is null ? "تأییدکننده" : $"{u.FirstName} {u.LastName}".Trim();
        }

        await dispatchNotifier.NotifyRejecterUrgentReapprovalAsync(submission, rejecterStep, rejecterName, ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> EndWorkflowAsync(
        FormSubmission submission,
        Guid actorUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        if (!FormWorkflowRejectionHelper.IsAwaitingSender(submission))
            return (false, "اتمام گردش در این مرحله مجاز نیست");

        if (!await IsDispatchSenderAsync(submission, actorUserId, isAdmin, ct))
            return (false, "فقط ارسال‌کننده لینک فرم می‌تواند گردش را خاتمه دهد");

        submission.IsArchived = true;
        submission.WorkflowRejectionJson = null;
        await db.SaveChangesAsync(ct);
        await dispatchNotifier.NotifyAfterWorkflowEndedBySenderAsync(submission, ct);
        await dispatchNotifier.NotifyResponderAfterWorkflowClosedAsync(submission, ct);
        return (true, null);
    }
}
