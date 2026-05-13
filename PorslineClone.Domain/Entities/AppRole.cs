using Microsoft.AspNetCore.Identity;

namespace PorslineClone.Domain.Entities;

public class AppRole : IdentityRole<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
