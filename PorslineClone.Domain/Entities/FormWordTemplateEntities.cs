namespace PorslineClone.Domain.Entities;

/// <summary>قالب Word متصل به فرم — تبدیل پاسخ‌ها به DOCX با جایگزینی placeholder.</summary>
public class FormWordTemplate
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? DocxFileName { get; set; }
    public string? DocxFilePath { get; set; }
    public string DetectedPlaceholdersJson { get; set; } = "[]";
    /// <summary>[{ placeholderKey, formFieldLabel?, source?: "adminSignature" | "adminStamp" }]</summary>
    public string FieldMappingsJson { get; set; } = "[]";
    public string? SignaturePlaceholderKey { get; set; }
    public string? SignatureImagePath { get; set; }
    public string? StampPlaceholderKey { get; set; }
    public string? StampImagePath { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

/// <summary>فایل Word تولیدشده برای یک پاسخ ثبت‌شده.</summary>
public class FormSubmissionWordDocument
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public FormSubmission Submission { get; set; } = null!;
    public Guid TemplateId { get; set; }
    public FormWordTemplate Template { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
