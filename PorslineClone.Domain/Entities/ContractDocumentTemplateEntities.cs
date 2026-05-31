namespace PorslineClone.Domain.Entities;

/// <summary>نوع فیلد فرم قالب قرارداد</summary>
public enum ContractTemplateFieldType
{
    Text = 1,
    TextArea = 2,
    Number = 3,
    Date = 4,
    Phone = 5,
    NationalId = 6,
    /// <summary>جایگاه امضای تأییدکننده در Word — هنگام تأیید پر می‌شود</summary>
    Signature = 7,
    /// <summary>شماره قرارداد — هنگام ثبت به‌صورت خودکار (مثلاً EN14040042)</summary>
    ContractNumber = 8,
    /// <summary>تصویر آپلودی کاربر — در Word جایگزین می‌شود</summary>
    Image = 9
}

/// <summary>قالب سند Word برای تولید قرارداد (جایگزین {{placeholder}})</summary>
public class ContractDocumentTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? ActiveVersionId { get; set; }
    public ContractDocumentTemplateVersion? ActiveVersion { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<ContractDocumentTemplateVersion> Versions { get; set; } = [];
    public ICollection<ContractDocumentTemplateField> Fields { get; set; } = [];
}

/// <summary>نسخه فایل Word قالب</summary>
public class ContractDocumentTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public ContractDocumentTemplate Template { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    /// <summary>JSON array of placeholder keys detected in docx</summary>
    public string DetectedPlaceholdersJson { get; set; } = "[]";
    public string? ChangeNote { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<ContractDocumentTemplateField> Fields { get; set; } = [];
}

/// <summary>تعریف فیلد فرم مرتبط با placeholder (هر نسخه Word فیلدهای خودش را دارد)</summary>
public class ContractDocumentTemplateField
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public ContractDocumentTemplate Template { get; set; } = null!;
    public Guid VersionId { get; set; }
    public ContractDocumentTemplateVersion Version { get; set; } = null!;
    /// <summary>کلید placeholder بدون آکولاد، مثلاً first_name</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public ContractTemplateFieldType FieldType { get; set; } = ContractTemplateFieldType.Text;
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>ترتیب نمایش در طراح (drag-drop)</summary>
    public string? DesignerOrderJson { get; set; }
    public string? DefaultValue { get; set; }
    public string? OptionsJson { get; set; }
}
