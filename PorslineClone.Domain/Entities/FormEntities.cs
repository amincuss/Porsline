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
    ImageUpload = 16,
    /// <summary>فایل راهنما (توسط سازنده فرم) — فقط مشاهده برای پاسخگو</summary>
    Guide = 17,
    /// <summary>عکس پرسنلی — حداکثر یکی در فرم؛ در نسخه اداری گوشه بالا راست</summary>
    PersonalPhoto = 18,
    /// <summary>مقدار ثابت — فقط طراحی؛ کلید placeholder و مقدار برای Word</summary>
    FixedConstant = 19
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
    /// <summary>مقدار پیش‌فرض نمایش‌داده‌شده هنگام پر کردن فرم</summary>
    public string? DefaultValue { get; set; }
    /// <summary>فیلد با مقدار پیش‌فرض قابل ویرایش توسط پاسخ‌دهنده نیست</summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>قالب گردش تأیید فرم (با نام یکتا)</summary>
public class FormWorkflowTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string StepsJson { get; set; } = "[]";
    public string? ActionDirectionKey { get; set; }
    public string? ActionDirectionLabel { get; set; }
    public string ActionAssigneeUserIdsJson { get; set; } = "[]";
    public string? CanvasLayoutJson { get; set; }
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
    /// <summary>کد پیگیری ۸ رقمی — پس از ثبت فرم در وب به پاسخگو پیامک می‌شود.</summary>
    public string? TrackingCode { get; set; }
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
    /// <summary>وضعیت اقدام پس از تأیید (JSON) — فقط در صورت فعال‌سازی فاز اقدام</summary>
    public string? PostApprovalJson { get; set; }
    /// <summary>پس از اتمام فاز اقدام از لیست گردش کار خارج می‌شود</summary>
    public bool IsArchived { get; set; }
    /// <summary>شماره دور گردش — ۰ قبل از اولین شروع؛ ۱ اولین گردش؛ ۲+ گردش مجدد پس از رد</summary>
    public int WorkflowRunCycle { get; set; }
    /// <summary>سوابق دورهای قبلی گردش (JSON)</summary>
    public string? WorkflowRunsHistoryJson { get; set; }
    /// <summary>رد قطعی در انتظار اقدام ارسال‌کننده یا تأیید مجدد</summary>
    public string? WorkflowRejectionJson { get; set; }
}

/// <summary>لینک سریع اقدام پس از تأیید کامل پاسخ فرم (بدون OTP)</summary>
public class FormActionLink
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
    public DateTime? ReminderSmsSentAtUtc { get; set; }
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
    /// <summary>گردش انتخاب‌شده هنگام ارسال؛ پس از ثبت کامل فرم به‌صورت خودکار شروع می‌شود.</summary>
    public Guid? WorkflowTemplateId { get; set; }
    /// <summary>کارشناسی که لینک را برای پاسخگو ارسال کرده (برای اعلان پس از ثبت فرم).</summary>
    public Guid? SentByUserId { get; set; }
}

public class FormUserAccess
{
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
