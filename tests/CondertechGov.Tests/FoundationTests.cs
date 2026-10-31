using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using CondertechGov.Tenancy;
using Xunit;
namespace CondertechGov.Tests;
public sealed class FoundationTests
{
    [Fact] public void TenantScopeCannotSwitchTenant()
    { var scope = new TenantScope(); scope.Resolve(Guid.NewGuid()); Assert.Throws<UnauthorizedAccessException>(() => scope.Resolve(Guid.NewGuid())); }
    [Fact] public void EmptyTenantIsRejected()
    { Assert.Throws<UnauthorizedAccessException>(() => new TenantScope().Resolve(Guid.Empty)); }
    [Fact] public void RenameChangesVersionAndTrimsName()
    { var unit = new OrganizationUnit(); var version = unit.Version; unit.Rename(" Fazenda ", version); Assert.Equal("Fazenda", unit.Name); Assert.NotEqual(version, unit.Version); }
    [Fact] public void RenameRejectsStaleVersion()
    { Assert.Throws<DomainException>(() => new OrganizationUnit().Rename("Fazenda", Guid.NewGuid())); }
    [Theory] [InlineData("")] [InlineData("  ")]
    public void RenameRejectsInvalidName(string name)
    { var unit = new OrganizationUnit(); Assert.Throws<DomainException>(() => unit.Rename(name, unit.Version)); }
    [Fact] public void UnitInputValidationRejectsInvalidCodeAndParent()
    { Assert.False(new CreateUnitValidator().Validate(new CreateUnit("../outro", "", Guid.Empty)).IsValid); }
    [Fact] public void ProvisioningRejectsExpiredContractAndInvalidSlug()
    { Assert.False(new ProvisionTenantValidator().Validate(new ProvisionTenant("../a", "", "invalid", "short", DateTimeOffset.UtcNow.AddDays(-1))).IsValid); }
    [Fact] public void HashIsDeterministicAndDoesNotStoreToken()
    { var hash = AuthenticationService.Hash("opaque-token"); Assert.Equal(hash, AuthenticationService.Hash("opaque-token")); Assert.Equal(64, hash.Length); Assert.DoesNotContain("opaque-token", hash); }
}
