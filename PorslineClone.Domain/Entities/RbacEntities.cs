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
    public bool UserCreateSmsEnabled { get; set; } = true;
    public bool ApprovalReferralSmsEnabled { get; set; } = true;
    public bool PublicFormRequireOtp { get; set; } = false;
}

public class InboxMessage
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Responder
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
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
