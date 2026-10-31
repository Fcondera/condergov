using CondertechGov.SharedKernel;
namespace CondertechGov.Infrastructure;
public sealed class TenantScope : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public void Resolve(Guid tenantId, Guid? userId = null)
    {
        if (tenantId == Guid.Empty || (TenantId != Guid.Empty && TenantId != tenantId))
            throw new UnauthorizedAccessException("Contexto de órgão inválido.");
        TenantId = tenantId; UserId = userId;
    }
}
