using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CondertechGov.Commercial;
using CondertechGov.Identity;
using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CondertechGov.Api;
public static class Endpoints
{
    public static void MapGovEndpoints(this WebApplication app, string adminKey)
    {
        var admin = app.MapGroup("/api/v1/admin").WithTags("Commercial");
        admin.MapPost("/tenants", async (HttpContext http, ProvisionTenant request, ProvisioningService service, CancellationToken ct) =>
        {
            if (!CheckAdmin(http, adminKey)) return Results.Unauthorized();
            var id = await service.CreateAsync(request, ct, ReadIdempotencyKey(http));
            return Results.Created($"/api/v1/admin/tenants/{id}", new { id });
        }).AllowAnonymous().RequireRateLimiting("authentication");
        admin.MapGet("/tenants", async (HttpContext http, GovDbContext db, CancellationToken ct) =>
            !CheckAdmin(http, adminKey) ? Results.Unauthorized() : Results.Ok(await db.Tenants.AsNoTracking().OrderBy(t => t.Slug).Take(100).Select(t => new { t.Id, t.Slug, t.Name, t.WriteEnabled, t.SubscriptionExpiresAt }).ToListAsync(ct)))
            .AllowAnonymous();
        admin.MapPut("/tenants/{id:guid}/subscription", async (Guid id, SubscriptionUpdate request, HttpContext http, TenantScope scope, GovDbContext db, CancellationToken ct) =>
        {
            if (!CheckAdmin(http, adminKey)) return Results.Unauthorized();
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (tenant is null) return Results.NotFound();
            scope.Resolve(id); tenant.WriteEnabled = request.WriteEnabled; tenant.SubscriptionExpiresAt = request.ExpiresAt;
            db.Record("subscription.update", id.ToString()); await db.SaveChangesAsync(ct); return Results.NoContent();
        }).AllowAnonymous();
        admin.MapPut("/tenants/{id:guid}/licenses/{module}", async (Guid id, string module, LicenseUpdate request, HttpContext http, TenantScope scope, GovDbContext db, CancellationToken ct) =>
        {
            if (!CheckAdmin(http, adminKey)) return Results.Unauthorized();
            if (module.Length is < 2 or > 60 || request.StartsAt >= request.EndsAt) return Results.BadRequest();
            if (!await db.Tenants.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            scope.Resolve(id);
            var license = await db.Licenses.SingleOrDefaultAsync(x => x.Module == module, ct);
            if (license is null) { license = new ModuleLicense { TenantId = id, Module = module }; db.Licenses.Add(license); }
            license.StartsAt = request.StartsAt; license.EndsAt = request.EndsAt;
            db.Record("license.update", module); await db.SaveChangesAsync(ct); return Results.NoContent();
        }).AllowAnonymous();

        var auth = app.MapGroup("/api/v1/auth").WithTags("Identity");
        auth.MapPost("/login", async (LoginRequest r, AuthenticationService s, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.TenantSlug) || string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrEmpty(r.Password) || r.Password.Length > 128) return Results.Unauthorized();
            var result = await s.LoginAsync(r, ct); return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).AllowAnonymous().RequireRateLimiting("authentication");
        auth.MapPost("/refresh", async (RefreshRequest r, AuthenticationService s, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.TenantSlug) || string.IsNullOrWhiteSpace(r.RefreshToken) || r.RefreshToken.Length > 256) return Results.Unauthorized();
            var result = await s.RefreshAsync(r, ct); return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).AllowAnonymous().RequireRateLimiting("authentication");
        auth.MapPost("/logout", async (ClaimsPrincipal principal, GovDbContext db, CancellationToken ct) =>
        {
            var id = Guid.Parse(principal.FindFirstValue("sid")!);
            var session = await db.Sessions.SingleAsync(x => x.Id == id, ct);
            session.RevokedAt = DateTimeOffset.UtcNow; session.Version = Guid.NewGuid(); db.Record("session.logout", id.ToString());
            await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        auth.MapGet("/me", async (TenantScope scope, GovDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.UserId, ct);
            return Results.Ok(new { u.Id, u.TenantId, u.Email, u.Permissions, u.TwoFactorEnabled });
        });
        auth.MapPost("/mfa/setup", async (PasswordConfirmation request, TenantScope scope, UserManager<GovUser> users,
            GovDbContext db, AuthenticationService authentication, CancellationToken ct) =>
        {
            var user = (await users.FindByIdAsync(scope.UserId.ToString()!))!;
            if (!await users.CheckPasswordAsync(user, request.Password)) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Results.Conflict();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await users.ResetAuthenticatorKeyAsync(user);
            var tokens = await authentication.ReissueAfterReauthenticationAsync(user, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { sharedKey = await users.GetAuthenticatorKeyAsync(user), issuer = "Condertech Gov", account = user.Email, tokens });
        }).RequireRateLimiting("authentication");
        auth.MapPost("/mfa/enable", async (TotpConfirmation request, TenantScope scope, UserManager<GovUser> users, GovDbContext db, CancellationToken ct) =>
        {
            var user = (await users.FindByIdAsync(scope.UserId.ToString()!))!;
            if (user.TwoFactorEnabled || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code)) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await users.SetTwoFactorEnabledAsync(user, true); await users.UpdateSecurityStampAsync(user);
            await db.Sessions.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Version, Guid.NewGuid()), ct);
            db.Record("mfa.enabled", user.Id.ToString()); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("authentication");

        var units = app.MapGroup("/api/v1/organization-units").WithTags("Organization");
        units.MapPost("/", async (CreateUnit r, HttpContext http, OrganizationService service, CancellationToken ct) =>
        { var unit = await service.CreateAsync(r, ct, ReadIdempotencyKey(http)); return Results.Created($"/api/v1/organization-units/{unit.Id}", UnitResponse.From(unit)); }).RequireAuthorization(Permissions.UnitsWrite);
        units.MapGet("/", async (GovDbContext db, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null, string sort = "code") =>
        {
            if (page < 1 || page > 100000 || pageSize is < 1 or > 100 || search?.Length > 200 || sort is not ("code" or "name")) return Results.BadRequest();
            var q = db.Units.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
            var total = await q.CountAsync(ct);
            q = sort == "name" ? q.OrderBy(x => x.Name).ThenBy(x => x.Id) : q.OrderBy(x => x.Code).ThenBy(x => x.Id);
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new Page<UnitResponse>(items.Select(UnitResponse.From).ToArray(), total, page, pageSize));
        }).RequireAuthorization(Permissions.UnitsRead);
        units.MapGet("/{id:guid}", async (Guid id, GovDbContext db, CancellationToken ct) =>
        { var u = await db.Units.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return u is null ? Results.NotFound() : Results.Ok(UnitResponse.From(u)); }).RequireAuthorization(Permissions.UnitsRead);
        units.MapPut("/{id:guid}", async (Guid id, RenameUnit r, GovDbContext db, CancellationToken ct) =>
        {
            var unit = await db.Units.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (unit is null) return Results.NotFound();
            if (unit.Version != r.Version) return Results.Conflict();
            unit.Rename(r.Name, r.Version); db.Record("unit.rename", id.ToString()); await db.SaveChangesAsync(ct); return Results.Ok(UnitResponse.From(unit));
        }).RequireAuthorization(Permissions.UnitsWrite);

        app.MapPost("/api/v1/users", CreateUser).RequireAuthorization(Permissions.UsersManage).WithTags("Identity");
        app.MapPut("/api/v1/users/{id:guid}/suspension", async (Guid id, SuspendUser r, ClaimsPrincipal actor, TenantScope scope, GovDbContext db, UserManager<GovUser> users, CancellationToken ct) =>
        {
            if (id == scope.UserId) return Results.BadRequest();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct); if (user is null) return Results.NotFound();
            if (user.Permissions.Any(p => !actor.HasClaim("permission", p))) return Results.Forbid();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            user.Suspended = r.Suspended; await users.UpdateSecurityStampAsync(user);
            await db.Sessions.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Version, Guid.NewGuid()), ct);
            db.Record("user.suspension", id.ToString()); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Permissions.UsersManage).WithTags("Identity");
        app.MapGet("/api/v1/audit-events", async (GovDbContext db, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            if (page < 1 || page > 100000 || pageSize is < 1 or > 100) return Results.BadRequest();
            return Results.Ok(new Page<AuditEvent>(await db.Audit.AsNoTracking().OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), await db.Audit.CountAsync(ct), page, pageSize));
        }).RequireAuthorization(Permissions.AuditRead).WithTags("Audit");
        app.MapFileEndpoints();
    }
    private static bool CheckAdmin(HttpContext http, string key)
    {
        // Never combine a tenant session with a platform credential.
        if (http.User.Identity?.IsAuthenticated == true) return false;
        var supplied = http.Request.Headers["X-Provisioning-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
    private static Guid? ReadIdempotencyKey(HttpContext http)
    {
        var header = http.Request.Headers["Idempotency-Key"].ToString();
        if (header.Length == 0) return null;
        if (!Guid.TryParse(header, out var key) || key == Guid.Empty) throw new DomainException("Idempotency-Key deve ser um UUID válido.");
        return key;
    }
    private static async Task<IResult> CreateUser(NewUser request, ClaimsPrincipal actor, GovDbContext db, TenantScope scope, UserManager<GovUser> users, CancellationToken ct)
    {
        if (request.Permissions is null || request.Permissions.Any(p => !Permissions.All.Contains(p)) ||
            string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(request.Email, out var mail) || mail.Address != request.Email ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 128) return Results.BadRequest();
        if (request.Permissions.Any(p => !actor.HasClaim("permission", p))) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var limit = await db.Tenants.Where(x => x.Id == scope.TenantId).Select(x => x.UserLimit).SingleAsync(ct);
        if (await db.Users.CountAsync(ct) >= limit) return Results.Problem(statusCode: 409, title: "Limite de usuários atingido.");
        var user = new GovUser { Id = Guid.NewGuid(), TenantId = scope.TenantId, Email = request.Email.Trim(),
            UserName = $"{scope.TenantId:N}:{request.Email.Trim()}", Permissions = request.Permissions.Distinct().ToArray() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) throw new DomainException(string.Join("; ", result.Errors.Select(x => x.Description)));
        db.Notifications.Add(new Notification { TenantId = scope.TenantId, Recipient = user.Email,
            Subject = "Conta Condertech Gov criada", Body = "Uma conta institucional foi criada. Solicite ao administrador o procedimento seguro de primeiro acesso." });
        db.Record("user.create", user.Id.ToString()); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Created($"/api/v1/users/{user.Id}", new { user.Id, user.Email, user.Permissions });
    }
}
public sealed record RenameUnit(string Name, Guid Version);
public sealed record PasswordConfirmation(string Password);
public sealed record TotpConfirmation(string Code);
public sealed record NewUser(string Email, string Password, string[] Permissions);
public sealed record SuspendUser(bool Suspended);
public sealed record SubscriptionUpdate(bool WriteEnabled, DateTimeOffset ExpiresAt);
public sealed record LicenseUpdate(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record UnitResponse(Guid Id, Guid TenantId, string Code, string Name, Guid? ParentId, Guid Version)
{
    public static UnitResponse From(CondertechGov.Tenancy.OrganizationUnit unit) => new(unit.Id, unit.TenantId, unit.Code, unit.Name, unit.ParentId, unit.Version);
}
