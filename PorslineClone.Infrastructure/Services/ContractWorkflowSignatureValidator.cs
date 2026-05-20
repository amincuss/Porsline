using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>تطابق تعداد placeholder امضا در قالب Word با تعداد تأییدکننده گردش</summary>
public static class ContractWorkflowSignatureValidator
{
    public static int CountApprovers(string? workflowStepsJson)
    {
        if (string.IsNullOrWhiteSpace(workflowStepsJson))
            return 0;

        var steps = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowStepsJson) ?? [];
        return steps.Count(s => s.UserId != Guid.Empty);
    }

    public static int CountApproversFromContractSteps(string? contractStepsJson)
    {
        if (string.IsNullOrWhiteSpace(contractStepsJson))
            return 0;

        var steps = ContractWorkflowProcessor.DeserializeSteps(contractStepsJson);
        return steps.Count(s => s.UserId != Guid.Empty);
    }

    public static async Task<int> CountSignatureFieldsAsync(
        AppDbContext db,
        Guid? templateId,
        Guid? versionId,
        CancellationToken ct = default)
    {
        if (templateId is null || templateId == Guid.Empty)
            return 0;

        var resolvedVersionId = versionId;
        if (resolvedVersionId is null || resolvedVersionId == Guid.Empty)
        {
            resolvedVersionId = await db.ContractDocumentTemplates
                .AsNoTracking()
                .Where(t => t.Id == templateId)
                .Select(t => t.ActiveVersionId)
                .FirstOrDefaultAsync(ct);
        }

        if (resolvedVersionId is null || resolvedVersionId == Guid.Empty)
            return 0;

        return await db.ContractDocumentTemplateFields
            .AsNoTracking()
            .Where(f => f.VersionId == resolvedVersionId && f.FieldType == ContractTemplateFieldType.Signature)
            .CountAsync(ct);
    }

    /// <summary>کلید placeholderهای امضا به ترتیب SortOrder (مرحله ۱ → اولین کلید، …).</summary>
    public static async Task<IReadOnlyList<string>> GetOrderedSignatureFieldKeysAsync(
        AppDbContext db,
        Guid? templateId,
        Guid? versionId,
        CancellationToken ct = default)
    {
        if (templateId is null || templateId == Guid.Empty)
            return [];

        var resolvedVersionId = versionId;
        if (resolvedVersionId is null || resolvedVersionId == Guid.Empty)
        {
            resolvedVersionId = await db.ContractDocumentTemplates
                .AsNoTracking()
                .Where(t => t.Id == templateId)
                .Select(t => t.ActiveVersionId)
                .FirstOrDefaultAsync(ct);
        }

        if (resolvedVersionId is null || resolvedVersionId == Guid.Empty)
            return [];

        return await db.ContractDocumentTemplateFields
            .AsNoTracking()
            .Where(f => f.VersionId == resolvedVersionId && f.FieldType == ContractTemplateFieldType.Signature)
            .OrderBy(f => f.SortOrder)
            .Select(f => f.Key)
            .ToListAsync(ct);
    }

    public static string? ValidateCounts(int signatureCount, int approverCount)
    {
        if (signatureCount == approverCount)
            return null;

        if (signatureCount == 0)
        {
            return approverCount == 0
                ? null
                : $"این قالب placeholder امضا ندارد اما گردش {approverCount} تأییدکننده دارد. در Word جایگاه امضا اضافه کنید یا گردش کوتاه‌تری انتخاب کنید.";
        }

        return $"تعداد placeholder امضا در قالب ({signatureCount}) با تعداد تأییدکننده گردش ({approverCount}) برابر نیست.";
    }

    public static async Task<string?> ValidateAsync(
        AppDbContext db,
        Guid? contractDocumentTemplateId,
        Guid? contractDocumentTemplateVersionId,
        string? workflowStepsJson,
        CancellationToken ct = default)
    {
        if (contractDocumentTemplateId is null || contractDocumentTemplateId == Guid.Empty)
            return null;

        var signatureCount = await CountSignatureFieldsAsync(
            db, contractDocumentTemplateId, contractDocumentTemplateVersionId, ct);
        var approverCount = CountApprovers(workflowStepsJson);
        return ValidateCounts(signatureCount, approverCount);
    }
}
