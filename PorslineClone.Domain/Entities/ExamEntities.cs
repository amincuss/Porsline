namespace PorslineClone.Domain.Entities;

public enum ExamQuestionType
{
    FourOption = 1,
    TwoOption = 2,
    Descriptive = 3,
}

public class ExamForm
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "آزمون بدون عنوان";
    public string? Description { get; set; }
    /// <summary>مدت آزمون به دقیقه — از شروع شرکت‌کننده</summary>
    public int DurationMinutes { get; set; } = 60;
    /// <summary>شروع بازهٔ مجاز شرکت (UTC)</summary>
    public DateTime? WindowStartAtUtc { get; set; }
    /// <summary>پایان بازهٔ مجاز شرکت (UTC)</summary>
    public DateTime? WindowEndAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
}

public class ExamQuestion
{
    public Guid Id { get; set; }
    public Guid ExamFormId { get; set; }
    public ExamForm ExamForm { get; set; } = null!;
    public ExamQuestionType QuestionType { get; set; }
    public string Label { get; set; } = "";
    public string? OptionsJson { get; set; }
    /// <summary>شماره گزینه صحیح (۰-based) — فقط برای سوالات گزینه‌ای</summary>
    public int? CorrectAnswerIndex { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ExamLink
{
    public Guid Id { get; set; }
    public Guid ExamFormId { get; set; }
    public ExamForm ExamForm { get; set; } = null!;
    public Guid? ExamDispatchId { get; set; }
    public ExamDispatch? ExamDispatch { get; set; }
    public Guid? ExamParticipantId { get; set; }
    public ExamParticipant? ExamParticipant { get; set; }
    public string Code { get; set; } = "";
    public string? ParticipantName { get; set; }
    public string? ParticipantMobile { get; set; }
    /// <summary>شروع بازهٔ مجاز — اگر خالی باشد از فرم آزمون خوانده می‌شود</summary>
    public DateTime? WindowStartAtUtc { get; set; }
    /// <summary>پایان بازهٔ مجاز — اگر خالی باشد از فرم آزمون خوانده می‌شود</summary>
    public DateTime? WindowEndAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UsedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>ارسال/زمان‌بندی آزمون برای گروه‌های آزمون‌دهنده</summary>
public class ExamDispatch
{
    public Guid Id { get; set; }
    public Guid ExamFormId { get; set; }
    public ExamForm ExamForm { get; set; } = null!;
    public DateTime WindowStartAtUtc { get; set; }
    public DateTime WindowEndAtUtc { get; set; }
    /// <summary>آرایه JSON از شناسه گروه‌ها</summary>
    public string GroupIdsJson { get; set; } = "[]";
    public int TotalParticipants { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    /// <summary>حداقل پاسخ صحیح برای قبولی</summary>
    public int PassingCorrectCount { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ExamLink> Links { get; set; } = new List<ExamLink>();
}

public class ExamSubmission
{
    public Guid Id { get; set; }
    public Guid ExamLinkId { get; set; }
    public ExamLink ExamLink { get; set; } = null!;
    public Guid ExamFormId { get; set; }
    public ExamForm ExamForm { get; set; } = null!;
    public string? AnswersJson { get; set; }
    public int? CorrectCount { get; set; }
    public int? ScorableQuestionCount { get; set; }
    public int? PassingCorrectCount { get; set; }
    public bool? IsPassed { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAutoSubmitted { get; set; }
}

/// <summary>گروه آزمون‌دهندگان — جدا از گروه کاربران سیستمی</summary>
public class ExamParticipantGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<ExamParticipantGroupMember> Members { get; set; } = new List<ExamParticipantGroupMember>();
}

public class ExamParticipantGroupMember
{
    public Guid ParticipantId { get; set; }
    public ExamParticipant Participant { get; set; } = null!;
    public Guid GroupId { get; set; }
    public ExamParticipantGroup Group { get; set; } = null!;
}

/// <summary>آزمون‌دهنده — بدون حساب ورود به سیستم</summary>
public class ExamParticipant
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string NationalCode { get; set; } = string.Empty;
    public string? PersonnelCode { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ExamParticipantGroupMember> GroupMembers { get; set; } = new List<ExamParticipantGroupMember>();
}
