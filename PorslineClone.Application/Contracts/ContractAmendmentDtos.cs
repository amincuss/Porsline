namespace PorslineClone.Application.Contracts;

/// <summary>وضعیت اصلاحیه پس از رد با «توقف زنجیره» — JSON روی Contract.AmendmentJson</summary>
public class ContractAmendmentStateDto
{
    /// <summary>creator_amendment | first_approver_amendment</summary>
    public string Phase { get; set; } = "";
    /// <summary>full | contract_amendment | needs_meeting</summary>
    public string RejectionType { get; set; } = "contract_amendment";
    /// <summary>waiting | in_progress | done</summary>
    public string AmendmentStatus { get; set; } = "waiting";
    public string? AmendmentNote { get; set; }
    public int RejectedAtStepOrder { get; set; }
    public Guid RejectedByUserId { get; set; }
    public Guid AssigneeUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>شماره دور اصلاحیه جاری</summary>
    public int Cycle { get; set; }
    /// <summary>شماره نسخه اصلاح‌شده آپلودشده توسط ایجادکننده</summary>
    public int? AmendedVersionNumber { get; set; }
    public DateTime? AmendedFileUploadedAtUtc { get; set; }
    /// <summary>مسیر فایل امضاشده قبل از شروع اصلاحیه (برای دانلود/پیش‌نمایش)</summary>
    public string? SignedFilePath { get; set; }
    public string? SignedPdfFilePath { get; set; }
    public string? SignedFileName { get; set; }
}

public record ContractAmendmentViewDto(
    string Phase,
    string RejectionType,
    string AmendmentStatus,
    string? AmendmentNote,
    int RejectedAtStepOrder,
    Guid RejectedByUserId,
    Guid AssigneeUserId,
    bool CanUpdateAmendment,
    bool RequiresAmendedFile,
    int? AmendedVersionNumber,
    bool CanUploadAmendedFile,
    DateTime? AmendedFileUploadedAtUtc);

public record ContractAmendmentUpdateRequest(
    string AmendmentStatus,
    string? Note);

public record ContractAmendmentRegenerateRequest(
    string FieldValuesJson,
    bool AutoSubmitToWorkflow = false);
