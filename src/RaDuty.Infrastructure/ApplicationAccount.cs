using Microsoft.AspNetCore.Identity;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class ApplicationAccount : IdentityUser<Guid>
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
