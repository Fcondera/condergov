using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using CondertechGov.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace CondertechGov.IntegrationTests;
[Collection("api")]
public sealed class FoundationApiTests(ApiFixture f)
{
    [Fact] public async Task DelegatedAccountManagerCannotGrantPermissionsTheyDoNotHave()
    {
        var created = await f.A.PostAsJsonAsync("/api/v1/users", new { email = "delegated@example.gov.br", password = ApiFixture.Password, permissions = new[] { Permissions.UsersManage } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var manager = await f.Login("municipio-a", "delegated@example.gov.br");
        var escalation = await manager.PostAsJsonAsync("/api/v1/users", new { email = "escalation@example.gov.br", password = ApiFixture.Password, permissions = new[] { Permissions.FilesRead } });
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);
        var admin = await f.A.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/v1/users/{admin.GetProperty("id").GetGuid()}/suspension", new { suspended = true })).StatusCode);
    }
    [Fact] public async Task ProvisioningIsIdempotentAndRejectsReusedKeyWithDifferentPayload()
    {
        using var client = f.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Provisioning-Key", ApiFactory.PlatformKey);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var request = new ProvisionTenant("idempotent-city", "Idempotent City", "admin@example.org", ApiFixture.Password, DateTimeOffset.UtcNow.AddYears(1));
        var one = await client.PostAsJsonAsync("/api/v1/admin/tenants", request);
        var two = await client.PostAsJsonAsync("/api/v1/admin/tenants", request);
        Assert.Equal(HttpStatusCode.Created, one.StatusCode); Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(await one.Content.ReadAsStringAsync(), await two.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/admin/tenants", request with { Name = "Changed" })).StatusCode);
    }
    [Fact] public async Task ConcurrentRefreshCannotLeaveAnActiveReplayedFamily()
    {
        using var client = f.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = "municipio-a", email = "admin@example.gov.br", password = ApiFixture.Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantSlug = "municipio-b", refreshToken = token })).StatusCode);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantSlug = "municipio-a", refreshToken = token })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var success = await results.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", success.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact] public async Task IdempotentUnitCreationReusesIdentifierAndRejectsChangedPayload()
    {
        using var client = f.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = f.A.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var payload = new CreateUnit("IDEMPOTENT", "Unidade idempotente", null);
        var one = await client.PostAsJsonAsync("/api/v1/organization-units", payload);
        var two = await client.PostAsJsonAsync("/api/v1/organization-units", payload);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal((await one.Content.ReadFromJsonAsync<OrganizationUnit>())!.Id, (await two.Content.ReadFromJsonAsync<OrganizationUnit>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/organization-units", payload with { Name = "Outro nome" })).StatusCode);
    }
    [Fact] public async Task FailedAdministratorCreationRollsBackTenant()
    {
        var response = await f.Platform.PostAsJsonAsync("/api/v1/admin/tenants", new ProvisionTenant("rollback-city", "Rollback", "admin@example.org", "alllowercase1234", DateTimeOffset.UtcNow.AddYears(1)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = f.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<GovDbContext>().Tenants.AnyAsync(x => x.Slug == "rollback-city"));
    }
    [Fact] public async Task DatabaseOptimisticConcurrencyRejectsLostUpdate()
    {
        using var one = f.Factory.Services.CreateScope(); using var two = f.Factory.Services.CreateScope();
        one.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantB);
        two.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantB);
        var db1 = one.ServiceProvider.GetRequiredService<GovDbContext>(); var db2 = two.ServiceProvider.GetRequiredService<GovDbContext>();
        var a = await db1.Units.SingleAsync(x => x.Code == "ROOT"); var b = await db2.Units.SingleAsync(x => x.Code == "ROOT");
        a.Rename("Nome atualizado", a.Version); b.Rename("Alteração concorrente", b.Version);
        await db1.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
    [Fact] public async Task OpenApiAndReadinessAreAvailable()
    {
        using var client = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(spec.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _));
    }
    [Fact] public async Task AnonymousAndPlatformCredentialsDoNotGrantContentAccess()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Platform.GetAsync("/api/v1/organization-units")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.PostAsJsonAsync("/api/v1/admin/tenants", new ProvisionTenant("blocked", "Blocked", "x@example.org", ApiFixture.Password, DateTimeOffset.UtcNow.AddYears(1)))).StatusCode);
    }
    [Fact] public async Task UnitsPersistAndCannotBeReadOrLinkedAcrossTenants()
    {
        var response = await f.A.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("SEC-TEST", "Secretaria A", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var unit = (await response.Content.ReadFromJsonAsync<OrganizationUnit>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.GetAsync($"/api/v1/organization-units/{unit.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.B.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("CHILD", "Child", unit.Id))).StatusCode);
        var page = (await f.B.GetFromJsonAsync<Page<OrganizationUnit>>("/api/v1/organization-units"))!;
        Assert.All(page.Items, x => Assert.Equal(f.TenantB, x.TenantId));
        Assert.Equal(unit.Name, (await f.A.GetFromJsonAsync<OrganizationUnit>($"/api/v1/organization-units/{unit.Id}"))!.Name);
    }
    [Fact] public async Task InvalidInputIsProblemDetailsAndDuplicateCodesConflict()
    {
        var bad = await f.A.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("../x", "", null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("application/problem+json", bad.Content.Headers.ContentType!.MediaType);
        Assert.True(bad.Headers.Contains("X-Correlation-ID"));
        var unit = new CreateUnit("DUPLICATE", "Unidade", null);
        Assert.Equal(HttpStatusCode.Created, (await f.A.PostAsJsonAsync("/api/v1/organization-units", unit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.A.PostAsJsonAsync("/api/v1/organization-units", unit)).StatusCode);
    }
    [Fact] public async Task StaleVersionCannotOverwriteUnit()
    {
        var response = await f.A.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("VERSION", "Antes", null));
        var unit = (await response.Content.ReadFromJsonAsync<OrganizationUnit>())!;
        Assert.Equal(HttpStatusCode.OK, (await f.A.PutAsJsonAsync($"/api/v1/organization-units/{unit.Id}", new { name = "Depois", version = unit.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.A.PutAsJsonAsync($"/api/v1/organization-units/{unit.Id}", new { name = "Perdido", version = unit.Version })).StatusCode);
    }
    [Fact] public async Task ReadOnlyPermissionCannotWriteAndSuspensionRevokesAccess()
    {
        var response = await f.A.PostAsJsonAsync("/api/v1/users", new { email = "reader@example.gov.br", password = ApiFixture.Password, permissions = new[] { Permissions.UnitsRead } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var reader = await f.Login("municipio-a", "reader@example.gov.br");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/organization-units")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("DENIED", "Denied", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await f.A.PutAsJsonAsync($"/api/v1/users/{id}/suspension", new { suspended = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reader.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.PutAsJsonAsync($"/api/v1/users/{id}/suspension", new { suspended = false })).StatusCode);
    }
    [Fact] public async Task RefreshRotationRejectsReplayAndRevokesFamily()
    {
        using var client = f.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = "municipio-a", email = "admin@example.gov.br", password = ApiFixture.Password });
        var original = await login.Content.ReadFromJsonAsync<JsonElement>();
        var raw = original.GetProperty("refreshToken").GetString();
        var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantSlug = "municipio-a", refreshToken = raw });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var next = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(raw, next.GetProperty("refreshToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantSlug = "municipio-a", refreshToken = raw })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact] public async Task SaveGuardAndCompositeForeignKeyRejectCrossTenantWrites()
    {
        using var scope = f.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<GovDbContext>();
        db.Units.Add(new OrganizationUnit { TenantId = f.TenantB, Code = "ILLEGAL", Name = "Illegal" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var parent = await f.B.GetFromJsonAsync<Page<OrganizationUnit>>("/api/v1/organization-units");
        db.Units.Add(new OrganizationUnit { TenantId = f.TenantA, Code = "ILLEGAL-FK", Name = "Illegal FK", ParentId = parent!.Items[0].Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task AuditIsTenantScopedAndWrittenWithMutation()
    {
        var page = (await f.A.GetFromJsonAsync<Page<AuditEvent>>("/api/v1/audit-events?pageSize=100"))!;
        Assert.All(page.Items, x => Assert.Equal(f.TenantA, x.TenantId));
        Assert.Contains(page.Items, x => x.Action == "tenant.provision");
        Assert.DoesNotContain(page.Items, x => string.IsNullOrEmpty(x.CorrelationId));
    }
    [Fact] public async Task CommercialSuspensionPreservesReadAccess()
    {
        var id = await f.Provision("expired-city");
        using var client = await f.Login("expired-city");
        await f.Platform.PutAsJsonAsync($"/api/v1/admin/tenants/{id}/subscription", new { writeEnabled = false, expiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/organization-units")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/organization-units", new CreateUnit("NEW", "New", null))).StatusCode);
    }
    [Fact] public async Task OtherTenantCannotAccessFileMetadataOrDownload()
    {
        Guid id;
        using (var scope = f.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantA);
            var db = scope.ServiceProvider.GetRequiredService<GovDbContext>();
            var file = new StoredFile { TenantId = f.TenantA, Name = "restrito.pdf", ObjectKey = $"{f.TenantA:N}/{Guid.NewGuid():N}", Sha256 = "hash" };
            db.Files.Add(file); await db.SaveChangesAsync(); id = file.Id;
        }
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.GetAsync($"/api/v1/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.GetAsync($"/api/v1/files/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.A.GetAsync($"/api/v1/files/{id}/download")).StatusCode);
    }
}
