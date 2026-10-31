using CondertechGov.SharedKernel;

namespace CondertechGov.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public bool WriteEnabled { get; set; } = true;
    public DateTimeOffset SubscriptionExpiresAt { get; set; }
    public int UserLimit { get; set; } = 50;
    public long StorageLimitBytes { get; set; } = 1_073_741_824;
    public Guid? ProvisioningKey { get; set; }
    public string? ProvisioningDigest { get; set; }
}
public sealed class OrganizationUnit : TenantEntity
{
    public Guid? RequestKey { get; set; }
    public string? RequestDigest { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public void Rename(string name, Guid expectedVersion)
    {
        if (Version != expectedVersion) throw new DomainException("A unidade foi alterada. Recarregue os dados.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new DomainException("Nome inválido.");
        Name = name.Trim(); Version = Guid.NewGuid();
    }
}
