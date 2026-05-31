using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Infrastructure.Services.Documents;

public class DocumentWorkflowAssignService(
    AppDbContext db,
    DocumentWorkflowProcessor workflowProcessor)
{
    public async Task<(bool Ok, string? Error, string? SuccessMessage)> AssignAsync(
        Document document,
        DocumentWorkflowTemplate template,
        AssignWorkflowRequest req,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var isRestart = DocumentWorkflowAccessRules.CanRestartWorkflowAfterReject(document);
        if (isRestart && !user.HasClaim("permission", "documents.workflow.restart")
            && !user.HasClaim("permission", "documents.workflow.update")
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
            DocumentWorkflowRunHistoryHelper.SnapshotCurrentRun(document);
            document.WorkflowRunCycle = Math.Max(1, document.WorkflowRunCycle) + 1;
            document.PostApprovalJson = null;
            document.WorkflowRejectionJson = null;

            var links = await db.DocumentApprovalLinks
                .Where(x => x.DocumentId == document.Id && x.IsActive)
                .ToListAsync(ct);
            foreach (var link in links)
                link.IsActive = false;
        }

        document.WorkflowTemplateId = template.Id;
        document.WorkflowName = template.Name;
        document.WorkflowStartedAtUtc = null;
        document.WorkflowScheduledStartAtUtc = mode == "scheduled" ? scheduledUtc : null;
        document.WorkflowStatus = DocumentWorkflowStatus.Pending;
        document.CurrentStepOrder = 1;
        var reviewCycle = isRestart ? document.WorkflowRunCycle : 0;
        document.StepsJson = WorkflowStepJsonHelper.Serialize(
            WorkflowStepBuilder.BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false, reviewCycle));

        await db.SaveChangesAsync(ct);

        var cycleLabel = document.WorkflowRunCycle > 1
            ? $" (دور {document.WorkflowRunCycle})"
            : "";

        if (mode == "now")
        {
            await db.Entry(document).ReloadAsync(ct);
            var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(document, ct);
            if (!ok) return (false, err ?? "شروع گردش ناموفق بود", null);
            return (true, null, isRestart
                ? $"گردش مجدد «{document.WorkflowName}»{cycleLabel} انتصاب و شروع شد"
                : $"گردش «{document.WorkflowName}» انتصاب و شروع شد");
        }

        if (mode == "scheduled")
        {
            return (true, null, isRestart
                ? $"گردش مجدد «{document.WorkflowName}»{cycleLabel} انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود"
                : $"گردش «{document.WorkflowName}» انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود");
        }

        return (true, null, isRestart
            ? $"گردش مجدد «{document.WorkflowName}»{cycleLabel} انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید"
            : $"گردش «{document.WorkflowName}» انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید");
    }
}
