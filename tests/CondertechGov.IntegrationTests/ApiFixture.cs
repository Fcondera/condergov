using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CondertechGov.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace CondertechGov.IntegrationTests;
public sealed class ApiFactory(string connection, string storageEndpoint, string smtpHost, int smtpPort) : WebApplicationFactory<Program>
{
    public const string PlatformKey = "integration-provisioning-key-32-characters-long";
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
        ["ConnectionStrings:Postgres"] = connection,
        ["Jwt:Key"] = new string('t', 80),
        ["Platform:ProvisioningKey"] = PlatformKey
        , ["RateLimit:AuthenticationPermitLimit"] = "1000"
        , ["Storage:Endpoint"] = storageEndpoint
        , ["Storage:AccessKey"] = "integration-access"
        , ["Storage:SecretKey"] = "integration-storage-secret"
        , ["Smtp:Host"] = smtpHost
        , ["Smtp:Port"] = smtpPort.ToString()
        , ["Smtp:EnableSsl"] = "false"
        , ["Smtp:From"] = "noreply@example.gov.br"
        , ["Scanner:Host"] = "127.0.0.1"
        , ["Scanner:Port"] = "1"
        }));
        return base.CreateHost(builder);
    }
}
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly IContainer storage = new ContainerBuilder("chrislusf/seaweedfs:4.48")
        .WithCommand("mini", "-dir=/data").WithEnvironment("AWS_ACCESS_KEY_ID", "integration-access")
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", "integration-storage-secret").WithEnvironment("S3_BUCKET", "gov-private")
        .WithPortBinding(8333, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8333)).Build();
    private readonly IContainer mail = new ContainerBuilder("axllent/mailpit:v1.27").WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025)).Build();
    public string StorageEndpoint => $"http://{storage.Hostname}:{storage.GetMappedPublicPort(8333)}";
    public string MailEndpoint => $"http://{mail.Hostname}:{mail.GetMappedPublicPort(8025)}";
    public ApiFactory Factory { get; private set; } = null!;
    public HttpClient Platform { get; private set; } = null!;
    public HttpClient A { get; private set; } = null!;
    public HttpClient B { get; private set; } = null!;
    public Guid TenantA { get; private set; }
    public Guid TenantB { get; private set; }
    public const string Password = "Strong!Password2026";
    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), storage.StartAsync(), mail.StartAsync());
        Factory = new ApiFactory(postgres.GetConnectionString(), StorageEndpoint, mail.Hostname, mail.GetMappedPublicPort(1025));
        Platform = Factory.CreateClient();
        Platform.DefaultRequestHeaders.Add("X-Provisioning-Key", ApiFactory.PlatformKey);
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<GovDbContext>().Database.MigrateAsync();
        TenantA = await Provision("municipio-a"); TenantB = await Provision("municipio-b");
        A = await Login("municipio-a"); B = await Login("municipio-b");
    }
    public async Task<Guid> Provision(string slug)
    {
        var response = await Platform.PostAsJsonAsync("/api/v1/admin/tenants", new ProvisionTenant(slug, "Prefeitura " + slug,
            "admin@example.gov.br", Password, DateTimeOffset.UtcNow.AddYears(1)));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    public async Task<HttpClient> Login(string slug, string email = "admin@example.gov.br", string? password = null)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = slug, email, password = password ?? Password });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
    public async Task DisposeAsync()
    { A?.Dispose(); B?.Dispose(); Platform?.Dispose(); if (Factory is not null) await Factory.DisposeAsync(); await Task.WhenAll(postgres.DisposeAsync().AsTask(), storage.DisposeAsync().AsTask(), mail.DisposeAsync().AsTask()); }
}
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
