using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Documents;
namespace PorslineClone.Api.Controllers;

public partial class AdminDashboardController
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var isAdmin = User.IsInRole("Admin");
        var canDocuments = User.HasClaim("permission", "forms.read");
        var canContracts = User.HasClaim("permission", "contracts.read")
            || User.HasClaim("permission", "contracts.read.all");
        var canForms = User.HasClaim("permission", "forms.read")
            || User.HasClaim("permission", "forms.read.all");
        var canApprovals = User.HasClaim("permission", "approvals.read");

        var user = await userManager.FindByIdAsync(userId.ToString());
        var displayName = user is null
            ? null
            : $"{user.FirstName} {user.LastName}".Trim();

        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);

        var kpis = new List<DashboardKpiDto>();
        var myTasks = new List<DashboardTaskItemDto>();
        var pendingApprovals = new List<DashboardTaskItemDto>();
        var activity = new List<DashboardActivityItemDto>();

        if (canDocuments)
        {
            var docScope = ScopeAccessibleDocuments(userId, isAdmin);
            var accessibleCount = await docScope.CountAsync(ct);
            var recentAdded = await docScope.CountAsync(d => d.CreatedAtUtc >= weekAgo, ct);
            var recentUpdated = await docScope.CountAsync(d => d.UpdatedAtUtc >= weekAgo, ct);
            var expiringSoon = await docScope.CountAsync(d =>
                d.ExpiresAtUtc != null && d.ExpiresAtUtc > now && d.ExpiresAtUtc <= now.AddDays(30), ct);

            var docPending = await CollectDocumentTasksAsync(docScope, userId, ct);

            kpis.Add(new("documents_accessible", "اسناد در دسترس", accessibleCount, "اسنادی که می‌توانید ببینید", "/admin/documents", "FolderOpen"));
            kpis.Add(new("documents_recent", "افزوده‌شده اخیر", recentAdded, "۷ روز گذشته", "/admin/documents", "FilePlus"));
            kpis.Add(new("documents_updated", "به‌روزرسانی اخیر", recentUpdated, "۷ روز گذشته", "/admin/documents", "RefreshCw"));
            if (expiringSoon > 0)
                kpis.Add(new("documents_expiring", "در آستانه انقضا", expiringSoon, "۳۰ روز آینده", "/admin/documents", "Clock"));

            foreach (var t in docPending)
            {
                if (t.TaskType.Contains("اقدام", StringComparison.Ordinal))
                    myTasks.Add(t);
                else
                    pendingApprovals.Add(t);
            }

            activity.AddRange(await BuildDocumentActivityAsync(docScope, userId, ct));
        }

        if (canContracts)
        {
            var contractPending = await CollectContractApprovalTasksAsync(userId, ct);
            pendingApprovals.AddRange(contractPending);
            var contractFeed = await BuildContractFeedAsync(userId, ct);
            foreach (var f in contractFeed.Take(5))
            {
                activity.Add(new DashboardActivityItemDto(
                    f.Id, "contract", "contract", f.Title, f.Message, null, f.AtUtc, f.LinkRoute ?? "/admin/contracts"));
            }
        }

        if (canForms || canApprovals)
        {
            var formPending = await CollectFormApprovalTasksAsync(userId, canApprovals, ct);
            pendingApprovals.AddRange(formPending);
            var formFeed = await BuildFormSubmissionFeedAsync(ct);
            foreach (var f in formFeed.Take(5))
            {
                activity.Add(new DashboardActivityItemDto(
                    f.Id, "form", "form", f.Title, f.Message, null, f.AtUtc, f.LinkRoute ?? "/admin/approvals"));
            }
        }

        var myTasksTotal = myTasks.Count;
        var pendingTotal = pendingApprovals.Count;

        if (myTasksTotal > 0)
            kpis.Insert(0, new("my_tasks", "کارهای من", myTasksTotal, "نیازمند اقدام شما", "/admin/actions", "ListTodo"));
        if (pendingTotal > 0)
            kpis.Insert(0, new("pending_approvals", "در انتظار تأیید", pendingTotal, "نوبت شما", "/admin/approvals", "CheckCircle"));

        var recentActivity = activity
            .OrderByDescending(a => a.AtUtc)
            .Take(15)
            .ToList();

        return Ok(new DashboardSummaryDto(
            string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            kpis.Take(6).ToList(),
            myTasks.OrderByDescending(t => t.IsOverdue).ThenBy(t => t.DueAtUtc).Take(8).ToList(),
            pendingApprovals.OrderByDescending(t => t.IsOverdue).ThenBy(t => t.DueAtUtc).Take(8).ToList(),
            recentActivity,
            myTasksTotal,
            pendingTotal));
    }

    private IQueryable<Document> ScopeAccessibleDocuments(Guid userId, bool isAdmin) =>
        db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsArchived)
            .Where(d => isAdmin
                || d.OwnerUserId == userId
                || (d.StepsJson != null && d.StepsJson.Contains(userId.ToString())));

    private async Task<List<DashboardTaskItemDto>> CollectDocumentTasksAsync(
        IQueryable<Document> scope,
        Guid userId,
        CancellationToken ct)
    {
        var rows = await scope
            .Where(d => d.WorkflowTemplateId != null || d.WorkflowStartedAtUtc != null)
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Take(60)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.Category,
                d.ReferenceNumber,
                d.WorkflowStatus,
                d.WorkflowStartedAtUtc,
                d.WorkflowScheduledStartAtUtc,
                d.CurrentStepOrder,
                d.StepsJson,
                d.PostApprovalJson,
                d.UpdatedAtUtc,
            })
            .ToListAsync(ct);

        var tasks = new List<DashboardTaskItemDto>();
        foreach (var d in rows)
        {
            var docEntity = new Document
            {
                Id = d.Id,
                WorkflowStatus = d.WorkflowStatus,
                WorkflowStartedAtUtc = d.WorkflowStartedAtUtc,
                CurrentStepOrder = d.CurrentStepOrder,
                PostApprovalJson = d.PostApprovalJson,
            };
            var steps = DocumentWorkflowProcessor.DeserializeSteps(d.StepsJson);
            var awaitingApproval = IsDocumentAwaitingUser(docEntity, steps, userId);
            var awaitingAction = DocumentActionPhaseHelper.IsAwaitingUserAction(docEntity, userId);

            if (awaitingApproval)
            {
                tasks.Add(new DashboardTaskItemDto(
                    d.Id.ToString(),
                    "document",
                    d.Title,
                    "تأیید گردش سند",
                    "in_progress",
                    "در انتظار تأیید شما",
                    d.WorkflowScheduledStartAtUtc,
                    false,
                    d.Category,
                    null,
                    $"/admin/documents/workflow-runs?id={d.Id}"));
            }

            if (awaitingAction)
            {
                tasks.Add(new DashboardTaskItemDto(
                    d.Id.ToString(),
                    "document",
                    d.Title,
                    "اقدام پس از تأیید",
                    "action",
                    "اقدام شما",
                    null,
                    false,
                    d.Category,
                    "high",
                    $"/admin/documents/workflow-runs?id={d.Id}"));
            }
        }

        return tasks;
    }

    private static bool IsDocumentAwaitingUser(Document doc, List<ApprovalStepDto> steps, Guid userId)
    {
        if (userId == Guid.Empty || doc.WorkflowStartedAtUtc is null) return false;
        if (doc.WorkflowStatus != DocumentWorkflowStatus.InProgress) return false;
        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, doc.CurrentStepOrder);
        return current is not null && current.UserId == userId;
    }

    private async Task<List<DashboardTaskItemDto>> CollectContractApprovalTasksAsync(Guid userId, CancellationToken ct)
    {
        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => !c.IsArchived && c.Status == ContractStatus.InProgress)
            .ApplyVisibleContracts(User)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(30)
            .Select(c => new { c.Id, c.ContractNumber, c.Title, c.SubjectPersonName, c.StepsJson, c.CreatedAtUtc })
            .ToListAsync(ct);

        var list = new List<DashboardTaskItemDto>();
        foreach (var c in contracts)
        {
            if (!IsPendingForUser(c.StepsJson, userId)) continue;
            var subject = ResolveContractSubject(c.Title, c.SubjectPersonName);
            list.Add(new DashboardTaskItemDto(
                c.Id.ToString(),
                "contract",
                $"{c.ContractNumber} — {subject}",
                "تأیید قرارداد",
                "pending",
                "در انتظار تأیید",
                null,
                false,
                null,
                null,
                "/admin/contracts"));
        }
        return list;
    }

    private async Task<List<DashboardTaskItemDto>> CollectFormApprovalTasksAsync(
        Guid userId,
        bool canApprovals,
        CancellationToken ct)
    {
        if (!canApprovals) return [];
        var submissions = await (
                from s in db.FormSubmissions.AsNoTracking().ApplyVisibleFormSubmissions(db, User)
                join f in db.Forms.AsNoTracking() on s.FormId equals f.Id
                where !f.IsDeleted && s.Status == FormSubmissionStatus.InProgress
                orderby s.SubmittedAtUtc descending
                select new { s.Id, s.StepsJson, s.SubmittedAtUtc, FormTitle = f.Title })
            .Take(30)
            .ToListAsync(ct);

        var list = new List<DashboardTaskItemDto>();
        foreach (var s in submissions)
        {
            if (!IsPendingForUser(s.StepsJson, userId)) continue;
            list.Add(new DashboardTaskItemDto(
                s.Id.ToString(),
                "form",
                s.FormTitle,
                "تأیید پاسخ فرم",
                "pending",
                "در انتظار تأیید",
                null,
                false,
                null,
                null,
                "/admin/approvals"));
        }
        return list;
    }

    private async Task<List<DashboardActivityItemDto>> BuildDocumentActivityAsync(
        IQueryable<Document> scope,
        Guid userId,
        CancellationToken ct)
    {
        var docIds = await scope.Select(d => d.Id).Take(500).ToListAsync(ct);
        if (docIds.Count == 0) return [];

        var rows = await db.DocumentActivities.AsNoTracking()
            .Where(a => docIds.Contains(a.DocumentId))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(12)
            .Select(a => new
            {
                a.Id,
                a.DocumentId,
                a.EventType,
                a.Message,
                a.CreatedAtUtc,
                a.ActorUserId,
            })
            .ToListAsync(ct);

        var actorIds = rows.Where(r => r.ActorUserId.HasValue).Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var actors = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var titles = await db.Documents.AsNoTracking()
            .Where(d => rows.Select(r => r.DocumentId).Distinct().Contains(d.Id))
            .Select(d => new { d.Id, d.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);

        return rows.Select(r =>
        {
            actors.TryGetValue(r.ActorUserId ?? Guid.Empty, out var actorName);
            titles.TryGetValue(r.DocumentId, out var title);
            return new DashboardActivityItemDto(
                r.Id.ToString(),
                "document",
                r.EventType,
                title ?? "سند",
                r.Message,
                string.IsNullOrWhiteSpace(actorName) ? null : actorName,
                r.CreatedAtUtc,
                $"/admin/documents?doc={r.DocumentId}");
        }).ToList();
    }
}
