namespace PorslineClone.Domain.Entities;

public enum FieldType
{
    ShortText = 1,
    LongText = 2,
    RadioGroup = 3,
    Dropdown = 4,
    Number = 5,
    Email = 6,
    Date = 7,
    Rating = 8,
    FileUpload = 9,
    Heading = 10,
    Paragraph = 11,
    WizardStepHeader = 12,
    Checkbox = 13,
    CheckboxGroup = 14,
    PersianDate = 15,
    ImageUpload = 16
}

public class Form
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "فرم بدون عنوان";
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAtUtc { get; set; }
    public string? UserId { get; set; }
    public bool IsDeleted { get; set; }
    public string QuestionDisplayMode { get; set; } = "all";
    public bool ApprovalEnabled { get; set; }
    public string? ApprovalWorkflowJson { get; set; }
    /// <summary>قالب گردش تأیید (مثل قرارداد) — در صورت تنظیم، مراحل از قالب کپی می‌شود</summary>
    public Guid? WorkflowTemplateId { get; set; }
    public FormWorkflowTemplate? WorkflowTemplate { get; set; }
    public string? WorkflowName { get; set; }
    public ICollection<FormField> Fields { get; set; } = new List<FormField>();
    public ICollection<FormSubmission> Submissions { get; set; } = new List<FormSubmission>();
}

public class FormField
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public FieldType FieldType { get; set; }
    public string Label { get; set; } = "";
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public int ColSpan { get; set; } = 12;
    public string? OptionsJson { get; set; }
    // Row/Column layout
    public string? RowId { get; set; }
    public int ColIndex { get; set; } = 0;
    public int RowColCount { get; set; } = 1;
    public string? ConditionsJson { get; set; }
    public int? UploadMaxSizeMb { get; set; }
}

/// <summary>قالب گردش تأیید فرم (با نام یکتا)</summary>
public class FormWorkflowTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string StepsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum FormSubmissionStatus
{
    /// <summary>گردش انتصاب شده ولی هنوز شروع نشده</summary>
    Pending = 1,
    InProgress = 2,
    Approved = 3,
    Rejected = 4,
    /// <summary>فرم ثبت شده؛ گردش هنوز برای این شخص ایجاد/انتصاب نشده</summary>
    Submitted = 5
}

public class FormSubmission
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public string? SubmitterName { get; set; }
    public string? SubmitterEmail { get; set; }
    public Guid? ResponderId { get; set; }
    public Guid? DispatchLinkId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public int CurrentStepOrder { get; set; } = 1;
    public FormSubmissionStatus Status { get; set; } = FormSubmissionStatus.Pending;
    public string? FieldsJson { get; set; }
    public string? StepsJson { get; set; }
    public Guid? WorkflowTemplateId { get; set; }
    public FormWorkflowTemplate? WorkflowTemplate { get; set; }
    public string? WorkflowName { get; set; }
    public DateTime? WorkflowStartedAtUtc { get; set; }
    public DateTime? WorkflowScheduledStartAtUtc { get; set; }
}

/// <summary>لینک تأیید عمومی برای پاسخ فرم (مثل قرارداد)</summary>
public class FormSubmissionApprovalLink
{
    public Guid Id { get; set; }
    public Guid FormSubmissionId { get; set; }
    public FormSubmission FormSubmission { get; set; } = null!;
    public Guid AssigneeUserId { get; set; }
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class FormDispatchLink
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Guid ResponderId { get; set; }
    public string ResponderFullName { get; set; } = "";
    public string ResponderMobileNumber { get; set; } = "";
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? OtpVerifiedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

public class FormUserAccess
{
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
