using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CondertechGov.Infrastructure;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<GovDbContext>
{
    public GovDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<GovDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=condertechgov;Username=gov")
        .Options, new TenantScope());
}
