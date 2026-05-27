using System.Text.Json;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormDispatchWorkflowHelper
{
    /// <summary>
    /// گردش ذخیره‌شده روی لینک ارسال را روی پاسخ اعمال می‌کند (وضعیت Pending، آماده شروع خودکار).
    /// </summary>
    public static void ApplyTemplateToSubmission(FormSubmission submission, FormWorkflowTemplate template)
    {
        submission.WorkflowTemplateId = template.Id;
        submission.WorkflowName = template.Name;
        submission.Status = FormSubmissionStatus.Pending;
        submission.CurrentStepOrder = 1;
        submission.WorkflowStartedAtUtc = null;
        submission.WorkflowScheduledStartAtUtc = null;
        submission.StepsJson = WorkflowStepJsonHelper.Serialize(
            WorkflowStepBuilder.BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false));
    }
}
