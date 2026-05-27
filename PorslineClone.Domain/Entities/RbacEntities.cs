namespace PorslineClone.Domain.Entities;

public class MenuItem
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconColor { get; set; } = "#6366F1";
    public string? Route { get; set; }
    public int Order { get; set; }
    public Guid? ParentId { get; set; }
}

public class Permission
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class RoleMenu
{
    public Guid RoleId { get; set; }
    public Guid MenuId { get; set; }
}

public class OtpCode
{
    public Guid Id { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ResponderOtpCode
{
    public Guid Id { get; set; }
    public Guid ResponderId { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}

public enum LoginMethod
{
    OtpOnly = 0,
    PasswordOnly = 1
}

public class SecuritySettings
{
    public int Id { get; set; }
    public bool EnableRateLimiting { get; set; } = true;
    public int MaxRequestsPerMinutePerIp { get; set; } = 20;
    public int MaxFailedOtpAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public bool MaskAuthErrors { get; set; } = true;
    public LoginMethod LoginMethod { get; set; } = LoginMethod.OtpOnly;
    /// <summary>مدت اعتبار لینک‌های عمومی بدون ورود (فرم، تأیید قرارداد/فرم) — روز</summary>
    public int AnonymousLinkExpiryDays { get; set; } = 7;
    /// <summary>الزام OTP برای باز کردن لینک ارسالی فرم</summary>
    public bool DispatchLinkRequireOtp { get; set; }
    /// <summary>مدت اعتبار توکن دسترسی (JWT) — دقیقه</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 180;
    /// <summary>مدت نگه‌داری نشست (رفرش‌توکن / کوکی) — روز؛ پس از آن کاربر باید دوباره وارد شود</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}

public class LoginAttempt
{
    public Guid Id { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string AttemptType { get; set; } = string.Empty; // otp_send | otp_verify
    public bool IsSuccess { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SmsSettings
{
    public int Id { get; set; }
    public bool OtpEnabled { get; set; } = true;
    public bool SurveySendEnabled { get; set; } = true;
    public bool SurveyCompletedNotificationEnabled { get; set; } = true;
    /// <summary>پس از ثبت فرم در وب، پیامک کد پیگیری به موبایل پاسخگو</summary>
    public bool FormSubmissionTrackingSmsEnabled { get; set; } = true;
    /// <summary>پس از شروع گردش تأیید، پیامک اطلاع‌رسانی به پاسخگو / ثبت‌کننده فرم</summary>
    public bool FormWorkflowStartedResponderSmsEnabled { get; set; } = true;
    public bool UserCreateSmsEnabled { get; set; } = true;
    public bool ApprovalReferralSmsEnabled { get; set; } = true;
    /// <summary>پس از تأیید نهایی گردش فرم، پیامک به کارشناس ارسال‌کننده لینک</summary>
    public bool FormWorkflowCompletedSenderSmsEnabled { get; set; } = true;
    /// <summary>پس از «اتمام کار» فاز اقدام، پیامک به ارسال‌کننده لینک فرم</summary>
    public bool FormActionPhaseCompletedSenderSmsEnabled { get; set; } = true;
    /// <summary>پس از اتمام فاز اقدام، پیامک تأیید نهایی به پاسخگو / ثبت‌کننده فرم</summary>
    public bool FormResponderApprovedSmsEnabled { get; set; } = true;
    /// <summary>پس از رد قطعی گردش فرم، پیامک به کارشناس ارسال‌کننده لینک</summary>
    public bool FormWorkflowRejectedSenderSmsEnabled { get; set; } = true;
    /// <summary>پس از رد قطعی گردش فرم، پیامک به پاسخگو / ثبت‌کننده</summary>
    public bool FormWorkflowRejectedResponderSmsEnabled { get; set; } = true;
    /// <summary>پس از هر تأیید در گردش قرارداد، پیامک به کاربر ثبت‌کننده قرارداد</summary>
    public bool ContractCreatorApprovalNotifySmsEnabled { get; set; } = true;
    /// <summary>پیامک به مسئول اصلاحیه پس از رد (ایجادکننده یا تأییدکننده اول)</summary>
    public bool ContractAmendmentAssigneeSmsEnabled { get; set; } = true;
    /// <summary>پیامک به تأییدکننده‌ای که رد کرده، پس از «انجام شد» اصلاحیه</summary>
    public bool ContractAmendmentReturnToRejecterSmsEnabled { get; set; } = true;
    /// <summary>پیامک اطلاع رد به ثبت‌کننده و طرف‌های مرتبط</summary>
    public bool ContractRejectionNotifySmsEnabled { get; set; } = true;
    /// <summary>پیامک به ایجادکننده قرارداد پس از «اتمام کار» در فاز اقدام (شماره، نوع، لینک مشاهده)</summary>
    public bool ContractActionCompletedCreatorSmsEnabled { get; set; } = true;
    /// <summary>ارسال خودکار یادآوری پس از اولین پیامک ارجاع تأیید</summary>
    public bool ApprovalReminderSmsEnabled { get; set; } = false;
    /// <summary>تأخیر یادآوری بر حسب روز (پس از اولین پیامک)</summary>
    public int ApprovalReminderDelayDays { get; set; } = 0;
    /// <summary>تأخیر یادآوری بر حسب ساعت (پس از اولین پیامک)</summary>
    public int ApprovalReminderDelayHours { get; set; } = 24;
    /// <summary>یادآوری پس از اتمام «اعتبار کل گردش» قرارداد (امضا نکرده‌اید)</summary>
    public bool WorkflowValidityReminderSmsEnabled { get; set; } = false;
    /// <summary>مهلت پس از یادآوری اعتبار گردش تا تعلیق خودکار (روز)</summary>
    public int WorkflowValiditySuspensionDelayDays { get; set; } = 0;
    public int WorkflowValiditySuspensionDelayHours { get; set; } = 24;
    public bool PublicFormRequireOtp { get; set; } = false;
}

public class InboxMessage
{
    public Guid Id { get; set; }
    /// <summary>گیرنده</summary>
    public Guid UserId { get; set; }
    /// <summary>فرستنده — null یعنی پیام سیستمی</summary>
    public Guid? SenderUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>متن ساده یا HTML</summary>
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentPath { get; set; }
    public bool IsRead { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Responder
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    /// <summary>کد ملی — یکتا برای پاسخگوهای فعال</summary>
    public string NationalCode { get; set; } = string.Empty;
    public UserGender? Gender { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<ResponderGroupMember> GroupMembers { get; set; } = new List<ResponderGroupMember>();
}

public class ResponderGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<ResponderGroupMember> Members { get; set; } = new List<ResponderGroupMember>();
}

public class ResponderGroupMember
{
    public Guid ResponderId { get; set; }
    public Responder Responder { get; set; } = null!;
    public Guid GroupId { get; set; }
    public ResponderGroup Group { get; set; } = null!;
}

public class UserGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<UserGroupMember> Members { get; set; } = new List<UserGroupMember>();
}

public class UserGroupMember
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid GroupId { get; set; }
    public UserGroup Group { get; set; } = null!;
}
