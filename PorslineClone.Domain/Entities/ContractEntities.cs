namespace PorslineClone.Domain.Entities;

public enum ContractStatus
{
    Pending = 1,
    InProgress = 2,
    Approved = 3,
    Rejected = 4
}

/// <summary>قالب گردش تأیید قرارداد (با نام یکتا)</summary>
public class ContractWorkflowTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string StepsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ContractType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ContractSettings
{
    public int Id { get; set; }
    public bool ApprovalEnabled { get; set; }
    public string? ApprovalWorkflowJson { get; set; }
    /// <summary>پیشوند شماره سند، مثلاً CNT</summary>
    public string DocumentNumberPrefix { get; set; } = "CNT";
    /// <summary>دوره شماره‌گذاری (yyyyMM) برای ریست سریال ماهانه</summary>
    public int DocumentSequencePeriod { get; set; }
    public int LastDocumentSequence { get; set; }
}

public class ContractVersion
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? PdfFilePath { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>نام کاربر ثبت‌کننده نسخه (ذخیره در زمان آپلود)</summary>
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ChangeNote { get; set; }
}

public class Contract
{
    public Guid Id { get; set; }
    /// <summary>شماره یکتای سند (تولید خودکار توسط سیستم)</summary>
    public string ContractNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NationalId { get; set; } = "";
    public string Phone { get; set; } = "";
    public Guid ContractTypeId { get; set; }
    public ContractType ContractType { get; set; } = null!;
    public Guid? WorkflowTemplateId { get; set; }
    public ContractWorkflowTemplate? WorkflowTemplate { get; set; }
    /// <summary>قالب Word که قرارداد از آن تولید شده</summary>
    public Guid? ContractDocumentTemplateId { get; set; }
    public ContractDocumentTemplate? ContractDocumentTemplate { get; set; }
    public Guid? ContractDocumentTemplateVersionId { get; set; }
    /// <summary>مقادیر فیلدهای قالب در زمان تولید (JSON)</summary>
    public string? TemplateFieldValuesJson { get; set; }
    /// <summary>نام گردش در زمان انتساب (برای نمایش)</summary>
    public string? WorkflowName { get; set; }
    public DateTime? WorkflowStartedAtUtc { get; set; }
    /// <summary>زمان برنامه‌ریزی‌شده برای شروع خودکار گردش (UTC)</summary>
    public DateTime? WorkflowScheduledStartAtUtc { get; set; }
    /// <summary>نام شخص موضوع قرارداد</summary>
    public string SubjectPersonName { get; set; } = "";
    public DateTime DateFromUtc { get; set; }
    public DateTime DateToUtc { get; set; }
    public string? FilePath { get; set; }
    /// <summary>نسخه بدون امضای تجمعی (منبع بازسازی PDF امضاشده)</summary>
    public string? OriginalFilePath { get; set; }
    /// <summary>نسخه PDF تولیدشده از قالب (اختیاری)</summary>
    public string? PdfFilePath { get; set; }
    public string? FileName { get; set; }
    public int CurrentVersionNumber { get; set; } = 1;
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>نام کاربر ایجادکننده قرارداد (ذخیره در زمان ثبت)</summary>
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ContractVersion> Versions { get; set; } = [];
    public int CurrentStepOrder { get; set; } = 1;
    public ContractStatus Status { get; set; } = ContractStatus.Pending;
    public string? StepsJson { get; set; }
}

/// <summary>لینک یک‌بار مصرف تأیید قرارداد بدون OTP (موبایل)</summary>
public class ContractApprovalLink
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public Guid AssigneeUserId { get; set; }
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
