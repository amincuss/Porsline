using System.ComponentModel.DataAnnotations;

namespace PorslineClone.Application.Contracts;

public record OtpRequestDto(
    [Required, RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل باید با 09 شروع شود و 11 رقم باشد.")]
    string MobileNumber);
public record OtpSendResultDto(bool IsSent, string? OtpCode, DateTime? ExpiresAtUtc);
public record OtpVerifyDto(
    [Required, RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل نامعتبر است.")]
    string MobileNumber,
    [Required, RegularExpression(@"^\d{4,6}$", ErrorMessage = "کد تایید باید بین 4 تا 6 رقم باشد.")]
    string Code);
public record AuthResponseDto(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, DateTime RefreshTokenExpiresAtUtc, string FullName, string RoleName);
public record RefreshTokenRequestDto([Required, MinLength(20)] string RefreshToken);
public record SecuritySettingsDto(
    bool EnableRateLimiting,
    [Range(1, 500)] int MaxRequestsPerMinutePerIp,
    [Range(1, 20)] int MaxFailedOtpAttempts,
    [Range(1, 120)] int LockoutMinutes,
    bool MaskAuthErrors,
    PorslineClone.Domain.Entities.LoginMethod LoginMethod,
    [Range(1, 365)] int AnonymousLinkExpiryDays,
    bool DispatchLinkRequireOtp,
    [Range(5, 1440)] int AccessTokenLifetimeMinutes,
    [Range(1, 90)] int RefreshTokenLifetimeDays);
public record LoginConfigDto(string LoginMethod, int OtpLifetimeSeconds);
public record PasswordLoginDto(
    [Required, RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل نامعتبر است.")] string MobileNumber,
    [Required, MinLength(6)] string Password);
public record SmsSettingsDto(
    bool OtpEnabled,
    bool SurveySendEnabled,
    bool SurveyCompletedNotificationEnabled,
    bool FormSubmissionTrackingSmsEnabled,
    bool UserCreateSmsEnabled,
    bool ApprovalReferralSmsEnabled,
    bool FormWorkflowStartedResponderSmsEnabled,
    bool FormWorkflowCompletedSenderSmsEnabled,
    bool FormActionPhaseCompletedSenderSmsEnabled,
    bool FormResponderApprovedSmsEnabled,
    bool FormWorkflowRejectedSenderSmsEnabled,
    bool FormWorkflowRejectedResponderSmsEnabled,
    bool ContractCreatorApprovalNotifySmsEnabled,
    bool ContractAmendmentAssigneeSmsEnabled,
    bool ContractAmendmentReturnToRejecterSmsEnabled,
    bool ContractRejectionNotifySmsEnabled,
    bool ContractActionCompletedCreatorSmsEnabled,
    bool ApprovalReminderSmsEnabled,
    int ApprovalReminderDelayDays,
    int ApprovalReminderDelayHours,
    bool WorkflowValidityReminderSmsEnabled,
    int WorkflowValiditySuspensionDelayDays,
    int WorkflowValiditySuspensionDelayHours);
public record DocumentSmsSettingsDto(
    bool DocumentApprovalReferralSmsEnabled,
    bool DocumentOwnerStepApprovalNotifySmsEnabled,
    bool DocumentWorkflowCompletedOwnerSmsEnabled,
    bool DocumentWorkflowRejectedOwnerSmsEnabled,
    bool DocumentPostApprovalAssigneeSmsEnabled,
    bool DocumentApprovalReminderSmsEnabled,
    int DocumentApprovalReminderDelayDays,
    int DocumentApprovalReminderDelayHours);
public record SiteSettingsDto(string? PublicBaseUrl, string? AdminPanelBaseUrl);
public record ProfileDto(string FirstName, string LastName, string MobileNumber, string NationalCode, string? AboutMe, string? AvatarUrl);
public record UpdateProfileDto([MaxLength(1000)] string? AboutMe);
public record InboxMessageDto(
    Guid Id,
    string Title,
    string Body,
    bool IsRead,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public record AdminInboxMessageDto(
    Guid Id,
    string Title,
    string Body,
    bool IsHtml,
    bool IsRead,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    bool IsSystemMessage,
    Guid? SenderUserId,
    string? SenderName,
    string? SenderEmail,
    Guid RecipientUserId,
    string? RecipientName,
    string? RecipientEmail,
    bool HasAttachment,
    string? AttachmentFileName);

public record InboxStatsDto(int Total, int Unread, int Archived);

public record AdminInboxStatsDto(
    int InboxTotal,
    int Unread,
    int Archived,
    int SystemUnread,
    int PersonalUnread,
    int SentTotal,
    int SentUnreadByRecipient);

public record MessageRecipientOptionDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Name,
    string Email,
    string? AvatarUrl);
public record SmsRequest(
    string MobileNumber,
    string Message,
    string? Source = null,
    Guid? UpdateExistingLogId = null,
    bool SkipThrottle = false);
public record CreateUserDto(
    [Required, MinLength(2), MaxLength(100)] string FirstName,
    [Required, MinLength(2), MaxLength(100)] string LastName,
    [Required, MaxLength(20)] string MobileNumber,
    [Required, MaxLength(50)] string NationalCode,
    List<Guid>? GroupIds = null,
    Guid? UserPositionId = null,
    string? PersonnelCode = null,
    string? Gender = null,
    int? SignatureDisplayDegree = null);

public record ImportUsersDto(List<Guid> DefaultGroupIds, List<ImportUserRowDto> Rows);
public record ImportUserRowDto(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? NationalCode,
    string? PersonnelCode = null,
    string? Gender = null,
    string? PositionName = null);

public record CreateGroupingUserDto(
    [Required] Guid GroupId,
    [Required, MaxLength(20)] string MobileNumber,
    string? FirstName = null,
    string? LastName = null,
    string? NationalCode = null,
    string? PersonnelCode = null);

public record ImportGroupingUsersDto(Guid GroupId, List<ImportGroupingUserRowDto> Rows);
public record ImportGroupingUserRowDto(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? NationalCode,
    string? PersonnelCode = null);

public record ContractPartyLookupItemDto(
    string NationalId,
    string FirstName,
    string LastName,
    string Phone,
    string SubjectPersonName,
    string? Title,
    Guid? ContractTypeId,
    int ContractCount);

public record ContractPartyDetailDto(
    string NationalId,
    string FirstName,
    string LastName,
    string Phone,
    string SubjectPersonName,
    string? Title,
    Guid? ContractTypeId,
    Dictionary<string, string>? TemplateFieldValues);
public record UpdateUserRoleDto([Required, MinLength(2), MaxLength(50)] string RoleName);
public record UpdateUserDto(
    [Required, MinLength(2), MaxLength(100)] string FirstName,
    [Required, MinLength(2), MaxLength(100)] string LastName,
    [Required, MaxLength(20)] string MobileNumber,
    [Required, MaxLength(50)] string NationalCode,
    List<Guid>? GroupIds = null,
    Guid? UserPositionId = null,
    string? PersonnelCode = null,
    string? Gender = null,
    int? SignatureDisplayDegree = null);
public record SetUserRolesDto([Required] List<string> RoleNames);
public record UpdateUserStatusDto(bool IsActive);
public record RoleItemDto(Guid Id, string Name, string DisplayName);
public record RolePermissionItemDto(Guid PermissionId, string PermissionName, bool Assigned);
public record SetRolePermissionDto([Required] string PermissionName, bool Assigned);
public record CreateRoleDto(
    [Required, MinLength(2), MaxLength(50), RegularExpression(@"^[a-zA-Z][a-zA-Z0-9_]+$")]
    string Name,
    [Required, MinLength(2), MaxLength(100)]
    string DisplayName);

public class MenuDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconColor { get; set; } = string.Empty;
    public string? Route { get; set; }
    public List<MenuDto> Children { get; set; } = new();
}
