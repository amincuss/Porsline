using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class WorkflowValidityHelper
{
    public static DateTime? ResolveEndsAtUtc(ContractWorkflowTemplate? template, DateTime workflowStartedAtUtc)
    {
        if (template is null) return null;
        var days = Math.Max(0, template.WorkflowValidityDays);
        var hours = Math.Max(0, template.WorkflowValidityHours);
        if (days == 0 && hours == 0) return null;
        return workflowStartedAtUtc.AddDays(days).AddHours(hours);
    }

    public static void ApplyValidityOnWorkflowStart(Contract contract, ContractWorkflowTemplate? template)
    {
        if (contract.WorkflowStartedAtUtc is null)
        {
            contract.WorkflowValidityEndsAtUtc = null;
            return;
        }

        contract.WorkflowValidityEndsAtUtc = ResolveEndsAtUtc(template, contract.WorkflowStartedAtUtc.Value);
        contract.WorkflowValidityReminderSentAtUtc = null;
        contract.SuspendedPendingUserId = null;
    }

    public static TimeSpan ResolveSuspensionGrace(SmsSettings settings)
    {
        var days = Math.Max(0, settings.WorkflowValiditySuspensionDelayDays);
        var hours = Math.Max(0, settings.WorkflowValiditySuspensionDelayHours);
        if (days == 0 && hours == 0)
            hours = 24;
        return TimeSpan.FromDays(days) + TimeSpan.FromHours(hours);
    }
}
