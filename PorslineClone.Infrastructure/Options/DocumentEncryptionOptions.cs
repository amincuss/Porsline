namespace PorslineClone.Infrastructure.Options;

public sealed class DocumentEncryptionOptions
{
    public const string SectionName = "DocumentEncryption";

    /// <summary>رمزنگاری at-rest برای فایل‌های سند جدید.</summary>
    public bool Enabled { get; set; }

    /// <summary>شناسه KEK فعال برای آپلود و چرخش (مثلاً v1، v2).</summary>
    public string PrimaryKeyId { get; set; } = "v1";
}
