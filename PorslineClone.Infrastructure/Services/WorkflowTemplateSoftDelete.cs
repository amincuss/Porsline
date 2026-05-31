using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class WorkflowTemplateSoftDelete
{
    public sealed record UsageCounts(int Contracts, int Forms, int FormSubmissions, int Documents = 0);

    public static Task<int> CountContractUsageAsync(AppDbContext db, Guid templateId, CancellationToken ct = default) =>
        db.Contracts.CountAsync(x => x.WorkflowTemplateId == templateId, ct);

    public static Task<int> CountFormUsageAsync(AppDbContext db, Guid templateId, CancellationToken ct = default) =>
        db.Forms.CountAsync(x => x.WorkflowTemplateId == templateId, ct);

    public static Task<int> CountFormSubmissionUsageAsync(AppDbContext db, Guid templateId, CancellationToken ct = default) =>
        db.FormSubmissions.CountAsync(x => x.WorkflowTemplateId == templateId, ct);

    public static Task<int> CountDocumentUsageAsync(AppDbContext db, Guid templateId, CancellationToken ct = default) =>
        db.Documents.CountAsync(x => x.WorkflowTemplateId == templateId, ct);

    public static string BuildDeleteMessage(string templateName, UsageCounts usage, bool isFormWorkflow, bool isDocumentWorkflow = false)
    {
        var parts = new List<string>();
        if (usage.Contracts > 0)
            parts.Add($"{usage.Contracts} قرارداد");
        if (usage.Forms > 0)
            parts.Add($"{usage.Forms} فرم");
        if (usage.FormSubmissions > 0)
            parts.Add($"{usage.FormSubmissions} پاسخ فرم");
        if (usage.Documents > 0)
            parts.Add($"{usage.Documents} سند");

        if (parts.Count == 0)
            return "گردش حذف شد (نرم) و از لیست انتخاب حذف می‌شود.";

        var where = isDocumentWorkflow ? "اسناد" : isFormWorkflow ? "فرم‌ها و پاسخ‌ها" : "قراردادها";
        return $"گردش «{templateName}» حذف شد (نرم). در {string.Join("، ", parts)} نام گردش همچنان نمایش داده می‌شود؛ در لیست {where} دیگر برای انتخاب نیست.";
    }
}
