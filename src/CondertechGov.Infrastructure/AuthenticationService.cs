using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CondertechGov.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CondertechGov.Infrastructure;
public sealed class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "condertech-gov";
    public string Audience { get; set; } = "condertech-gov-api";
}
public sealed class AuthenticationService(GovDbContext db, TenantScope scope, UserManager<GovUser> users,
    SignInManager<GovUser> signIn, IOptions<JwtSettings> settings)
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<TokenPair?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.TenantSlug, ct);
        if (tenant is null) return null;
        scope.Resolve(tenant.Id);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == users.NormalizeEmail(request.Email), ct);
        if (user is null || user.Suspended) return null;
        scope.Resolve(tenant.Id, user.Id);
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return null;
        if (user.TwoFactorEnabled && (request.Totp is null || !await users.VerifyTwoFactorTokenAsync(user,
            users.Options.Tokens.AuthenticatorTokenProvider, request.Totp)))
        {
            await users.AccessFailedAsync(user);
            return null;
        }
        await users.ResetAccessFailedCountAsync(user);
        var pair = Create(user);
        db.Record("session.login", user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return pair;
    }
    public async Task<TokenPair?> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.TenantSlug, ct);
        if (tenant is null) return null;
        scope.Resolve(tenant.Id);
        var hash = Hash(request.RefreshToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (session is null) return null;
        var now = DateTimeOffset.UtcNow;
        if (session.RevokedAt is not null)
        {
            await RevokeFamily(session.FamilyId, ct);
            db.Record("session.replay", session.Id.ToString());
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (session.ExpiresAt <= now) return null;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == session.UserId, ct);
        if (user is null || user.Suspended || session.SecurityStamp != user.SecurityStamp || await users.IsLockedOutAsync(user)) return null;
        scope.Resolve(tenant.Id, user.Id);
        var pair = Create(user, session.FamilyId);
        session.RevokedAt = now;
        session.ReplacedBy = db.Sessions.Local.Single(x => x.TokenHash == Hash(pair.RefreshToken)).Id;
        session.Version = Guid.NewGuid();
        db.Record("session.refresh", session.Id.ToString());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            await RevokeFamily(session.FamilyId, ct);
            return null;
        }
        return pair;
    }
    private Task<int> RevokeFamily(Guid familyId, CancellationToken ct) => db.Sessions.Where(x => x.FamilyId == familyId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Version, Guid.NewGuid()), ct);

    public async Task<TokenPair> ReissueAfterReauthenticationAsync(GovUser user, CancellationToken ct)
    {
        await db.Sessions.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Version, Guid.NewGuid()), ct);
        var pair = Create(user);
        db.Record("session.reauthenticate", user.Id.ToString()); await db.SaveChangesAsync(ct); return pair;
    }

    private TokenPair Create(GovUser user, Guid? family = null)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var session = new RefreshSession { TenantId = user.TenantId, UserId = user.Id, TokenHash = Hash(raw), SecurityStamp = user.SecurityStamp!,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7), FamilyId = family ?? Guid.NewGuid() };
        db.Sessions.Add(session);
        var config = settings.Value;
        List<Claim> claims = [new("sub", user.Id.ToString()), new("tenant", user.TenantId.ToString()),
            new("stamp", user.SecurityStamp!), new("sid", session.Id.ToString()), new("jti", Guid.NewGuid().ToString())];
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));
        var jwt = new JwtSecurityToken(config.Issuer, config.Audience, claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Key)), SecurityAlgorithms.HmacSha512));
        return new TokenPair(new JwtSecurityTokenHandler().WriteToken(jwt), raw);
    }
}
