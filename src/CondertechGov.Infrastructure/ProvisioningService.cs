using System.Data;
using CondertechGov.Identity;
using CondertechGov.SharedKernel;
using CondertechGov.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CondertechGov.Infrastructure;
public sealed record ProvisionTenant(string Slug, string Name, string AdminEmail, string AdminPassword,
    DateTimeOffset SubscriptionExpiresAt, int UserLimit = 50, long StorageLimitBytes = 1_073_741_824);
public sealed class ProvisionTenantValidator : AbstractValidator<ProvisionTenant>
{
    public ProvisionTenantValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9][a-z0-9-]{2,62}$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
        RuleFor(x => x.SubscriptionExpiresAt).GreaterThan(_ => DateTimeOffset.UtcNow);
        RuleFor(x => x.UserLimit).InclusiveBetween(1, 100000);
        RuleFor(x => x.StorageLimitBytes).GreaterThan(0);
    }
}
public sealed class ProvisioningService(GovDbContext db, TenantScope scope, UserManager<GovUser> users,
    IValidator<ProvisionTenant> validator, IConfiguration config)
{
    public async Task<Guid> CreateAsync(ProvisionTenant request, CancellationToken ct, Guid? idempotencyKey = null)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        string? digest = null;
        if (idempotencyKey.HasValue)
        {
            digest = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config["Platform:ProvisioningKey"]!), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            var existing = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.ProvisioningKey == idempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.ProvisioningDigest != digest) throw new ConflictException("Chave de idempotência já usada com outros dados.");
                return existing.Id;
            }
        }
        var tenant = new Tenant { Slug = request.Slug, Name = request.Name.Trim(), SubscriptionExpiresAt = request.SubscriptionExpiresAt,
            UserLimit = request.UserLimit, StorageLimitBytes = request.StorageLimitBytes, ProvisioningKey = idempotencyKey, ProvisioningDigest = digest };
        scope.Resolve(tenant.Id);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        var user = new GovUser { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = request.AdminEmail.Trim(),
            UserName = $"{tenant.Id:N}:{request.AdminEmail.Trim()}", Permissions = Permissions.All.ToArray() };
        var result = await users.CreateAsync(user, request.AdminPassword);
        if (!result.Succeeded) throw new DomainException(string.Join("; ", result.Errors.Select(x => x.Description)));
        db.Units.Add(new OrganizationUnit { TenantId = tenant.Id, Code = "ROOT", Name = tenant.Name });
        db.Record("tenant.provision", tenant.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return tenant.Id;
    }
}
