namespace PorslineClone.Domain.Entities;

public enum DocumentNodeType
{
    Folder = 1,
    File = 2,
}

public enum DocumentAccessLevel
{
    Public = 1,
    Internal = 2,
    Confidential = 3,
    HighlySecret = 4,
}

public enum DocumentPermissionSubjectType
{
    User = 1,
    Role = 2,
}

public enum DocumentPermissionLevel
{
    Viewer = 1,
    Editor = 2,
    Manager = 3,
    Owner = 4,
}

public enum DocumentShareScope
{
    AnyoneWithLink = 1,
    OrganizationOnly = 2,
    SpecificUsers = 3,
}

/// <summary>وضعیت استخراج متن از فایل (بدون OCR).</summary>
public enum DocumentTextProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}

public enum DocumentWorkflowStatus
{
    None = 0,
    Pending = 1,
    InProgress = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>لایه بایگانی: گرم (دسترسی سریع) یا سرد (نگهداری بلندمدت).</summary>
public enum DocumentArchiveTier
{
    None = 0,
    Warm = 1,
    Cold = 2,
}

/// <summary>وضعیت چرخه عمر سند.</summary>
public enum DocumentLifecycleStatus
{
    Active = 0,
    Obsolete = 1,
    Archived = 2,
    PendingDeletion = 3,
}

public class DocumentFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public DocumentFolder? Parent { get; set; }
    public bool IsDeleted { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Document
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public DocumentFolder Folder { get; set; } = null!;
    public string Title { get; set; } = "";
    /// <summary>دسته‌بندی موضوعی (مثلاً مکاتبات، قرارداد).</summary>
    public string Category { get; set; } = "Correspondence";
    public Guid? OrganizationalUnitId { get; set; }
    public DocumentSystemOrganizationalUnit? OrganizationalUnit { get; set; }
    public Guid? ProjectId { get; set; }
    public DocumentSystemProject? Project { get; set; }
    public DateTime? DocumentDateUtc { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ManualReferenceNumber { get; set; }
    public string? Description { get; set; }
    public DocumentAccessLevel AccessLevel { get; set; } = DocumentAccessLevel.Internal;
    public bool IsDeleted { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DocumentWorkflowStatus WorkflowStatus { get; set; } = DocumentWorkflowStatus.None;
    public Guid? WorkflowTemplateId { get; set; }
    public DocumentWorkflowTemplate? WorkflowTemplate { get; set; }
    public string? WorkflowName { get; set; }
    public string? StepsJson { get; set; }
    public int CurrentStepOrder { get; set; }
    public DateTime? WorkflowStartedAtUtc { get; set; }
    public DateTime? WorkflowScheduledStartAtUtc { get; set; }
    public string? PostApprovalJson { get; set; }
    public int WorkflowRunCycle { get; set; }
    public string? WorkflowRunsHistoryJson { get; set; }
    public string? WorkflowRejectionJson { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? RetentionPolicyId { get; set; }
    public DocumentRetentionPolicy? RetentionPolicy { get; set; }
    public DocumentArchiveTier ArchiveTier { get; set; } = DocumentArchiveTier.None;
    public DocumentLifecycleStatus LifecycleStatus { get; set; } = DocumentLifecycleStatus.Active;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public bool LegalHold { get; set; }
    public string? LegalHoldReason { get; set; }
    public DateTime? LegalHoldStartedAtUtc { get; set; }
    public Guid? LegalHoldByUserId { get; set; }
    public bool IsObsolete { get; set; }
    public DateTime? ObsoleteAtUtc { get; set; }
    public string? ObsoleteReason { get; set; }
    public DateTime? LifecycleWarningSentAtUtc { get; set; }
    public DateTime? ScheduledArchiveAtUtc { get; set; }
    public DateTime? ScheduledDeleteAtUtc { get; set; }
    public bool LongTermRetention { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentTag> Tags { get; set; } = [];
}

/// <summary>سیاست نگهداری و بایگانی اسناد (Retention Policy).</summary>
public class DocumentRetentionPolicy
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>اگر مقدار داشته باشد فقط برای این دسته‌بندی اعمال می‌شود.</summary>
    public string? CategoryMatch { get; set; }
    public DocumentAccessLevel? AccessLevelMatch { get; set; }
    /// <summary>روز پس از ایجاد/تاریخ سند تا آرشیو گرم خودکار.</summary>
    public int? ArchiveAfterDays { get; set; }
    /// <summary>روز پس از آرشیو گرم تا انتقال به بایگانی سرد.</summary>
    public int? MoveToColdAfterDays { get; set; }
    /// <summary>روز پس از ایجاد/تاریخ سند تا واجد حذف شدن.</summary>
    public int? DeleteAfterDays { get; set; }
    public int ExpirationWarningDays { get; set; } = 30;
    public bool LongTermRetention { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>تنظیمات سراسری چرخه عمر اسناد (یک ردیف).</summary>
public class DocumentLifecycleSettings
{
    public Guid Id { get; set; }
    public Guid? DefaultRetentionPolicyId { get; set; }
    public DocumentRetentionPolicy? DefaultRetentionPolicy { get; set; }
    public bool AutoProcessEnabled { get; set; } = true;
    public int DefaultExpirationWarningDays { get; set; } = 30;
    public int ProcessIntervalHours { get; set; } = 6;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>قالب گردش تأیید سند (با نام یکتا)</summary>
public class DocumentWorkflowTemplate
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

/// <summary>لینک تأیید عمومی برای سند</summary>
public class DocumentApprovalLink
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public Guid AssigneeUserId { get; set; }
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReminderSmsSentAtUtc { get; set; }
}

public class DocumentSystemTag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentSystemCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>واحد سازمانی برای طبقه‌بندی اسناد.</summary>
public class DocumentSystemOrganizationalUnit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>پروژه برای طبقه‌بندی اسناد.</summary>
public class DocumentSystemProject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? ContentHashSha256 { get; set; }
    public string? ChangeLog { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>فایل با AES-256-GCM و envelope encryption (DEK + KEK) ذخیره شده است.</summary>
    public bool IsEncrypted { get; set; }
    /// <summary>شناسه نسخه Master Key (KEK) برای چرخش کلید.</summary>
    public string? EncryptionKeyId { get; set; }
    /// <summary>Nonce/IV فایل (۱۲ بایت، Base64).</summary>
    public string? FileNonceBase64 { get; set; }
    /// <summary>DEK رمزشده با KEK — payload: nonce(12) + ciphertext(32) + tag(16).</summary>
    public string? EncryptedDekBase64 { get; set; }
    public DocumentVersionText? TextIndex { get; set; }
}

/// <summary>متن استخراج‌شده از نسخه فایل برای جستجوی Full-Text.</summary>
public class DocumentVersionText
{
    public Guid DocumentVersionId { get; set; }
    public DocumentVersion Version { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public string? ExtractedText { get; set; }
    public string? NormalizedText { get; set; }
    public DocumentTextProcessingStatus ProcessingStatus { get; set; } = DocumentTextProcessingStatus.Pending;
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public int CharCount { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string Tag { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentActivity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string EventType { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Reason { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentPermissionConfig
{
    public DocumentNodeType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public bool InheritFromParent { get; set; } = true;
    public Guid UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentPermissionEntry
{
    public Guid Id { get; set; }
    public DocumentNodeType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public DocumentPermissionSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public DocumentPermissionLevel Level { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentShareLink
{
    public Guid Id { get; set; }
    public DocumentNodeType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public DocumentShareScope Scope { get; set; } = DocumentShareScope.OrganizationOnly;
    public string Token { get; set; } = "";
    public string? SpecificSubjectIdsJson { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
