using System.Text.Json.Serialization;

namespace PorslineClone.Application.Contracts;

public record WorkflowStepDto(
    string Id,
    int Order,
    Guid UserId,
    string? Note,
    string OnReject = "stop",
    double? PosX = null,
    double? PosY = null,
    int? ApprovalDeadlineDays = null,
    int? ApprovalDeadlineHours = null);

public record SaveWorkflowRequest(bool Enabled, List<WorkflowStepDto> Steps);

public record WorkflowSettingsDto(
    bool Enabled,
    List<WorkflowStepDto> Steps,
    Guid? WorkflowTemplateId = null,
    string? WorkflowName = null,
    bool UseTemplate = false);

public record WorkflowCanvasLayoutDto(double StartX, double StartY, double EndX, double EndY);

public record SaveWorkflowTemplateRequest(
    string Name,
    List<WorkflowStepDto> Steps,
    string? ActionDirectionKey = null,
    List<Guid>? ActionAssigneeUserIds = null,
    WorkflowCanvasLayoutDto? CanvasLayout = null,
    int WorkflowValidityDays = 0,
    int WorkflowValidityHours = 0);

public record ContractWorkflowTemplateListItemDto(
    Guid Id,
    string Name,
    int StepCount,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId = null,
    string? CreatedByName = null);

public record ContractWorkflowTemplateDetailDto(
    Guid Id,
    string Name,
    bool IsActive,
    List<WorkflowStepDto> Steps,
    string? ActionDirectionKey = null,
    string? ActionDirectionLabel = null,
    List<Guid>? ActionAssigneeUserIds = null,
    WorkflowCanvasLayoutDto? CanvasLayout = null,
    int WorkflowValidityDays = 0,
    int WorkflowValidityHours = 0);

public record FormWorkflowTemplateListItemDto(
    Guid Id,
    string Name,
    int StepCount,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId = null,
    string? CreatedByName = null);

public record FormWorkflowTemplateDetailDto(
    Guid Id,
    string Name,
    bool IsActive,
    List<WorkflowStepDto> Steps,
    string? ActionDirectionKey = null,
    string? ActionDirectionLabel = null,
    List<Guid>? ActionAssigneeUserIds = null,
    WorkflowCanvasLayoutDto? CanvasLayout = null);

public record DocumentWorkflowTemplateListItemDto(
    Guid Id,
    string Name,
    int StepCount,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId = null,
    string? CreatedByName = null);

public record DocumentWorkflowTemplateDetailDto(
    Guid Id,
    string Name,
    bool IsActive,
    List<WorkflowStepDto> Steps,
    string? ActionDirectionKey = null,
    string? ActionDirectionLabel = null,
    List<Guid>? ActionAssigneeUserIds = null,
    WorkflowCanvasLayoutDto? CanvasLayout = null);

public record SaveFormWorkflowLinkRequest(string? WorkflowTemplateId, bool ConnectWorkflow);

public record FormFieldValueDto(string Label, string Value);

public class ApprovalStepDto
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? UserEmail { get; set; }
    public string Status { get; set; } = "waiting";
    public string? Comment { get; set; }
    public DateTime? ActionAt { get; set; }
    public string OnReject { get; set; } = "stop";
    public string? Note { get; set; }
    /// <summary>مهلت تأیید این مرحله (روز) — ۰ یعنی استفاده از پیش‌فرض تنظیمات پیامک</summary>
    public int ApprovalDeadlineDays { get; set; }
    /// <summary>مهلت تأیید این مرحله (ساعت) — ۰ یعنی استفاده از پیش‌فرض تنظیمات پیامک</summary>
    public int ApprovalDeadlineHours { get; set; }
    /// <summary>full | contract_amendment | needs_meeting — نوع رد هنگام رد مرحله</summary>
    public string? RejectionType { get; set; }
    /// <summary>تعداد دفعات بازگشت پس از اصلاحیه برای تأیید مجدد</summary>
    public int ReviewCycle { get; set; }
    public string? LastRejectionComment { get; set; }
    public string? LastRejectionType { get; set; }
    public DateTime? LastRejectedAtUtc { get; set; }
    /// <summary>اولین بازدید از لینک تأیید ارسالی (UTC)</summary>
    public DateTime? ApprovalLinkOpenedAtUtc { get; set; }
    /// <summary>مسیر نسبی تصویر امضا در زمان تأیید (اسنپ‌شات) — در StepsJson ذخیره می‌شود</summary>
    public string? SignatureImagePath { get; set; }
    /// <summary>درجه نمایش امضا در زمان تأیید</summary>
    public int? SignatureDisplayDegree { get; set; }
    /// <summary>نام امضاکننده در زمان تأیید</summary>
    public string? UserFirstName { get; set; }
    /// <summary>نام خانوادگی امضاکننده در زمان تأیید</summary>
    public string? UserLastName { get; set; }
    /// <summary>سمت امضاکننده در زمان تأیید</summary>
    public string? UserPositionTitle { get; set; }
    /// <summary>جنسیت تأییدکننده در زمان تأیید — ۱ مرد، ۲ زن</summary>
    public int? UserGender { get; set; }

    /// <summary>آدرس نمایش امضا — فقط در پاسخ API ساخته می‌شود، در StepsJson ذخیره نمی‌شود</summary>
    [JsonIgnore]
    public string? SignatureUrl { get; set; }
}

public record AssignWorkflowRequest(string WorkflowTemplateId, string? StartMode, string? ScheduledStartAtUtc);

public record BulkAssignFormWorkflowRequest(
    string WorkflowTemplateId,
    string? StartMode,
    string? ScheduledStartAtUtc,
    List<Guid>? SubmissionIds = null,
    bool AssignWholeGroup = false,
    Guid? GroupId = null,
    bool UngroupedOnly = false);
