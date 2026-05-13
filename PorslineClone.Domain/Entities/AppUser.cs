using Microsoft.AspNetCore.Identity;

namespace PorslineClone.Domain.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string NationalCode { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsSoftDeleted { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AboutMe { get; set; }
    public ICollection<UserGroupMember> GroupMembers { get; set; } = new List<UserGroupMember>();
}
