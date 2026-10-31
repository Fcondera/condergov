using CondertechGov.SharedKernel;
using CondertechGov.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CondertechGov.Infrastructure;
public sealed record CreateUnit(string Code, string Name, Guid? ParentId);
public sealed class CreateUnitValidator : AbstractValidator<CreateUnit>
{
    public CreateUnitValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentId).NotEqual(Guid.Empty).When(x => x.ParentId.HasValue);
    }
}
public sealed class OrganizationService(GovDbContext db, ITenantContext scope, IValidator<CreateUnit> validator)
{
    public async Task<OrganizationUnit> CreateAsync(CreateUnit request, CancellationToken ct, Guid? idempotencyKey = null)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        if (idempotencyKey.HasValue)
        {
            var existing = await db.Units.SingleOrDefaultAsync(x => x.RequestKey == idempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.RequestDigest != digest) throw new ConflictException("Chave de idempotência já usada com outros dados.");
                return existing;
            }
        }
        if (request.ParentId.HasValue && !await db.Units.AnyAsync(x => x.Id == request.ParentId, ct))
            throw new DomainException("Unidade superior indisponível.");
        var unit = new OrganizationUnit { TenantId = scope.TenantId, Code = request.Code.Trim(), Name = request.Name.Trim(), ParentId = request.ParentId,
            RequestKey = idempotencyKey, RequestDigest = digest };
        db.Units.Add(unit); db.Record("unit.create", unit.Id.ToString());
        await db.SaveChangesAsync(ct); return unit;
    }
}
