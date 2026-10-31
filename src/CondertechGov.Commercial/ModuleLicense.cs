using CondertechGov.SharedKernel;
namespace CondertechGov.Commercial;
public sealed class ModuleLicense : TenantEntity
{
    public string Module { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}
