using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CondertechGov.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace CondertechGov.IntegrationTests;
[Collection("api")]
public sealed class PublicIntegrationApiTests(ApiFixture f)
{
    [Fact] public async Task ReferenceQueriesRequireLoginAndNeverUsePlatformKeyAsTenantAccess()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Platform.GetAsync("/api/v1/integrations/ibge/states")).StatusCode);
    }
    [Fact] public async Task AuthenticatedQueryMapsProviderPayloadAndUsesCache()
    {
        using var handler = new ApiHandler(HttpStatusCode.OK, """[{"id":13,"nome":"Amazonas","sigla":"AM"}]""");
        await using var factory = f.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddHttpClient("public-data-Ibge").ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = f.A.DefaultRequestHeaders.Authorization;
        var first = await client.GetFromJsonAsync<ExternalData<BrazilianState[]>>("/api/v1/integrations/ibge/states");
        var second = await client.GetFromJsonAsync<ExternalData<BrazilianState[]>>("/api/v1/integrations/ibge/states");
        Assert.Equal("AM", first!.Data.Single().Abbreviation); Assert.True(second!.FromCache); Assert.Equal(1, handler.Calls);
        var status = await client.GetFromJsonAsync<PublicIntegrationStatus[]>("/api/v1/integrations");
        Assert.Contains(status!, s => s.Provider == "Ibge" && s.LastSuccessAt != null && s.Status == "available");
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schema = spec.GetProperty("paths").GetProperty("/api/v1/integrations/ibge/states")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
    }
    [Theory] [InlineData(429, 503)] [InlineData(500, 502)]
    public async Task UpstreamFailureReturnsSanitizedProblemDetails(int upstreamStatus, int expected)
    {
        using var handler = new ApiHandler((HttpStatusCode)upstreamStatus, "upstream secret debug details");
        await using var factory = f.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddHttpClient("public-data-Ibge").ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = f.A.DefaultRequestHeaders.Authorization;
        var response = await client.GetAsync("/api/v1/integrations/ibge/states");
        Assert.Equal(expected, (int)response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Ibge", problem.GetProperty("provider").GetString());
        Assert.True(problem.TryGetProperty("correlationId", out _)); Assert.DoesNotContain("upstream secret", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task InvalidExternalIdentifiersAreRejectedWithoutCallingUpstream()
    {
        using var handler = new ApiHandler(HttpStatusCode.OK, "{}");
        await using var factory = f.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddHttpClient("public-data-ViaCep").ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = f.A.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/integrations/viacep/addresses/invalid")).StatusCode);
        Assert.Equal(0, handler.Calls);
    }
    private sealed class ApiHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("X-Provisioning-Key"));
            Calls++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
