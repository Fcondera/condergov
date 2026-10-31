using CondertechGov.Commercial;
using CondertechGov.Identity;
using CondertechGov.SharedKernel;
using CondertechGov.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CondertechGov.Infrastructure;

public sealed class GovDbContext(DbContextOptions<GovDbContext> options, ITenantContext tenant)
    : IdentityDbContext<GovUser, IdentityRole<Guid>, Guid>(options)
{
    public Guid CurrentTenant => tenant.TenantId;
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<OrganizationUnit> Units => Set<OrganizationUnit>();
    public DbSet<RefreshSession> Sessions => Set<RefreshSession>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ModuleLicense> Licenses => Set<ModuleLicense>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Tenant>().HasIndex(x => x.ProvisioningKey).IsUnique();
        b.Entity<Tenant>().Property(x => x.Slug).HasMaxLength(63);
        b.Entity<Tenant>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<GovUser>().HasQueryFilter(x => x.TenantId == CurrentTenant);
        b.Entity<GovUser>().HasIndex(x => x.NormalizedUserName).IsUnique();
        b.Entity<GovUser>().HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
        b.Entity<GovUser>().HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Entity<GovUser>().HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        Configure<OrganizationUnit>(b); Configure<RefreshSession>(b); Configure<AuditEvent>(b);
        Configure<StoredFile>(b); Configure<Notification>(b); Configure<ModuleLicense>(b);
        b.Entity<OrganizationUnit>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Entity<OrganizationUnit>().HasIndex(x => new { x.TenantId, x.RequestKey }).IsUnique();
        b.Entity<OrganizationUnit>().Property(x => x.Code).HasMaxLength(50);
        b.Entity<OrganizationUnit>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<OrganizationUnit>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<OrganizationUnit>().HasOne<OrganizationUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RefreshSession>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<RefreshSession>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<RefreshSession>().HasOne<GovUser>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEvent>().HasIndex(x => new { x.TenantId, x.At });
        b.Entity<StoredFile>().HasIndex(x => x.ObjectKey).IsUnique();
        b.Entity<ModuleLicense>().HasIndex(x => new { x.TenantId, x.Module }).IsUnique();
    }
    private void Configure<T>(ModelBuilder b) where T : TenantEntity
    {
        b.Entity<T>().HasKey(x => new { x.TenantId, x.Id });
        b.Entity<T>().HasQueryFilter(x => x.TenantId == CurrentTenant);
        b.Entity<T>().HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Record(string action, string resource) => Audit.Add(new AuditEvent
    { TenantId = CurrentTenant, ActorId = tenant.UserId, Action = action, Resource = resource, CorrelationId = tenant.CorrelationId });

    public override Task<int> SaveChangesAsync(CancellationToken ct = default) => SaveChangesAsync(true, ct);
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ValidateWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    private void ValidateWrites()
    {
        foreach (var e in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var id = e.Entity switch { TenantEntity te => te.TenantId, GovUser u => u.TenantId, _ => (Guid?)null };
            if (id.HasValue && (CurrentTenant == Guid.Empty || id != CurrentTenant)) throw new UnauthorizedAccessException("Gravação fora do órgão atual.");
            if (id.HasValue && e.State != EntityState.Added && (Guid)e.Property("TenantId").OriginalValue! != CurrentTenant)
                throw new UnauthorizedAccessException("Alteração de órgão proibida.");
            if (e.Entity is AuditEvent && e.State != EntityState.Added) throw new UnauthorizedAccessException("Auditoria é imutável.");
        }
    }
}
