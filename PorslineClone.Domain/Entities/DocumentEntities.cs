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

public class DocumentFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
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
    public string Category { get; set; } = "Correspondence";
    public DateTime? DocumentDateUtc { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ManualReferenceNumber { get; set; }
    public string? Description { get; set; }
    public DocumentAccessLevel AccessLevel { get; set; } = DocumentAccessLevel.Internal;
    public bool IsDeleted { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentTag> Tags { get; set; } = [];
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
