using CondertechGov.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace CondertechGov.Identity;

public sealed class GovUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public bool Suspended { get; set; }
    public string[] Permissions { get; set; } = [];
}
public sealed class RefreshSession : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBy { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn = 600);
public sealed record LoginRequest(string TenantSlug, string Email, string Password, string? Totp);
public sealed record RefreshRequest(string TenantSlug, string RefreshToken);
