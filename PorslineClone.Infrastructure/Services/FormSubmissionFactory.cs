using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormSubmissionFactory
{
    /// <summary>
    /// پاسخ فرم را ذخیره می‌کند. گردش خطی فرم (ApprovalEnabled) بلافاصله شروع می‌شود.
    /// گردش قالب روی لینک ارسال پس از ثبت در PublicFormsController اعمال و خودکار شروع می‌شود.
    /// بدون گردش در ارسال، از «فرم کاربران» می‌توان گردش را منتصب کرد.
    /// </summary>
    public static FormSubmission Create(
        Form form,
        List<FormFieldValueDto> fieldValues,
        string? submitterName,
        string? submitterMobile,
        Guid? responderId = null,
        Guid? dispatchLinkId = null)
    {
        var inlineSteps = WorkflowStepBuilder.BuildApprovalStepsFromInline(form.ApprovalWorkflowJson, startImmediately: true);
        var hasInline = form.ApprovalEnabled && inlineSteps.Count > 0 && form.WorkflowTemplateId is null;

        return new FormSubmission
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            SubmitterName = submitterName,
            SubmitterEmail = submitterMobile,
            ResponderId = responderId,
            DispatchLinkId = dispatchLinkId,
            SubmittedAtUtc = DateTime.UtcNow,
            CurrentStepOrder = hasInline ? 1 : 0,
            Status = hasInline ? FormSubmissionStatus.InProgress : FormSubmissionStatus.Submitted,
            FieldsJson = JsonSerializer.Serialize(fieldValues),
            StepsJson = hasInline ? WorkflowStepJsonHelper.Serialize(inlineSteps) : null,
            WorkflowStartedAtUtc = hasInline ? DateTime.UtcNow : null,
        };
    }
}
