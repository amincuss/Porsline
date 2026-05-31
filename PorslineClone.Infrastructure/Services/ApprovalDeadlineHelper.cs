using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class ApprovalDeadlineHelper
{
    public static TimeSpan ResolveDeadline(ApprovalStepDto step, SmsSettings settings)
    {
        var stepDays = Math.Max(0, step.ApprovalDeadlineDays);
        var stepHours = Math.Max(0, step.ApprovalDeadlineHours);
        if (stepDays > 0 || stepHours > 0)
            return TimeSpan.FromDays(stepDays) + TimeSpan.FromHours(stepHours);

        return ResolveDefaultDelay(settings);
    }

    public static TimeSpan ResolveDefaultDelay(SmsSettings settings)
    {
        var days = Math.Max(0, settings.ApprovalReminderDelayDays);
        var hours = Math.Max(0, settings.ApprovalReminderDelayHours);
        if (days == 0 && hours == 0)
            hours = 24;
        return TimeSpan.FromDays(days) + TimeSpan.FromHours(hours);
    }

    public static TimeSpan ResolveDocumentDeadline(ApprovalStepDto step, SmsSettings settings)
    {
        var stepDays = Math.Max(0, step.ApprovalDeadlineDays);
        var stepHours = Math.Max(0, step.ApprovalDeadlineHours);
        if (stepDays > 0 || stepHours > 0)
            return TimeSpan.FromDays(stepDays) + TimeSpan.FromHours(stepHours);

        return ResolveDocumentDefaultDelay(settings);
    }

    public static TimeSpan ResolveDocumentDefaultDelay(SmsSettings settings)
    {
        var days = Math.Max(0, settings.DocumentApprovalReminderDelayDays);
        var hours = Math.Max(0, settings.DocumentApprovalReminderDelayHours);
        if (days == 0 && hours == 0)
            hours = 24;
        return TimeSpan.FromDays(days) + TimeSpan.FromHours(hours);
    }

    public static bool IsDue(DateTime referralSentAtUtc, TimeSpan deadline, DateTime nowUtc)
        => referralSentAtUtc + deadline <= nowUtc;

    public static string FormatDeadlineFa(TimeSpan deadline)
    {
        var parts = new List<string>();
        if (deadline.TotalDays >= 1)
            parts.Add($"{(int)deadline.TotalDays} روز");
        var hours = (int)deadline.TotalHours % 24;
        if (hours > 0 || parts.Count == 0)
            parts.Add($"{hours} ساعت");
        return string.Join(" و ", parts);
    }
}
