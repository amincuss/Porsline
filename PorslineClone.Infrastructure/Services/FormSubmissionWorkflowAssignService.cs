using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormSubmissionWorkflowAssignService(
    AppDbContext db,
    FormWorkflowProcessor workflowProcessor)
{
    public async Task<(bool Ok, string? Error, string? SuccessMessage)> AssignAsync(
        FormSubmission submission,
        FormWorkflowTemplate template,
        AssignWorkflowRequest req,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var isRestart = FormSubmissionWorkflowAccessRules.CanRestartWorkflowAfterReject(submission);
        if (isRestart && !user.HasClaim("permission", "responders.userforms.workflow.restart")
            && !user.HasClaim("permission", "responders.userforms.workflow")
            && !user.HasClaim("permission", "forms.update"))
            return (false, "مجوز «گردش مجدد» ندارید", null);

        var mode = (req.StartMode ?? "manual").Trim().ToLowerInvariant();
        DateTime? scheduledUtc = null;
        if (mode == "scheduled")
        {
            if (string.IsNullOrWhiteSpace(req.ScheduledStartAtUtc) ||
                !DateTime.TryParse(req.ScheduledStartAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return (false, "تاریخ شروع گردش نامعتبر است", null);

            scheduledUtc = parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
            if (scheduledUtc <= DateTime.UtcNow)
                return (false, "تاریخ شروع باید در آینده باشد", null);
        }

        if (isRestart)
        {
            FormWorkflowRunHistoryHelper.SnapshotCurrentRun(submission);
            submission.WorkflowRunCycle = Math.Max(1, submission.WorkflowRunCycle) + 1;
            submission.IsArchived = false;
            submission.PostApprovalJson = null;

            var links = await db.FormSubmissionApprovalLinks
                .Where(x => x.FormSubmissionId == submission.Id && x.IsActive)
                .ToListAsync(ct);
            foreach (var link in links)
                link.IsActive = false;
        }

        submission.WorkflowTemplateId = template.Id;
        submission.WorkflowName = template.Name;
        submission.WorkflowStartedAtUtc = null;
        submission.WorkflowScheduledStartAtUtc = mode == "scheduled" ? scheduledUtc : null;
        submission.Status = FormSubmissionStatus.Pending;
        submission.CurrentStepOrder = 1;
        var reviewCycle = isRestart ? submission.WorkflowRunCycle : 0;
        submission.StepsJson = WorkflowStepJsonHelper.Serialize(
            WorkflowStepBuilder.BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false, reviewCycle));

        await db.SaveChangesAsync(ct);

        var cycleLabel = submission.WorkflowRunCycle > 1
            ? $" (دور {submission.WorkflowRunCycle})"
            : "";

        if (mode == "now")
        {
            await db.Entry(submission).ReloadAsync(ct);
            var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(submission, ct);
            if (!ok) return (false, err ?? "شروع گردش ناموفق بود", null);
            return (true, null, isRestart
                ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب و شروع شد"
                : $"گردش «{submission.WorkflowName}» انتصاب و شروع شد");
        }

        if (mode == "scheduled")
        {
            return (true, null, isRestart
                ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود"
                : $"گردش «{submission.WorkflowName}» انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود");
        }

        return (true, null, isRestart
            ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید"
            : $"گردش «{submission.WorkflowName}» انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید");
    }
}
