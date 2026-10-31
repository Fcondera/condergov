using System.Net;
using System.Text;
using CondertechGov.Infrastructure.PublicData;
using CondertechGov.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;
namespace CondertechGov.Tests;

public sealed class PublicDataTests
{
    private const string Address = """{"cep":"69005-010","localidade":"Manaus","uf":"AM","ibge":"1302603","logradouro":"Rua de teste"}""";
    private const string Municipality = """{"id":1302603,"nome":"Manaus","regiao-imediata":{"regiao-intermediaria":{"UF":{"sigla":"AM"}}}}""";
    [Fact] public async Task NormalizedCepUsesSharedPublicCacheAndKeepsFetchTimestamp()
    {
        using var handler = new StubHandler((request, _) =>
        { Assert.Equal("https://viacep.com.br/ws/69005010/json/", request.RequestUri!.AbsoluteUri); return Task.FromResult(Reply(Address)); });
        using var query = Create(handler);
        var first = await query.PostalAddressAsync("69005-010", default);
        var cached = await query.PostalAddressAsync("69005010", default);
        Assert.Equal("Manaus", first.Data.City); Assert.False(first.FromCache); Assert.True(cached.FromCache);
        Assert.Equal(first.FetchedAt, cached.FetchedAt); Assert.Equal(1, handler.Calls);
        Assert.Equal("available", query.Status().Single(x => x.Provider == "ViaCep").Status);
    }
    [Theory] [InlineData("69005")] [InlineData("https://attacker")] [InlineData("69005010/other")] [InlineData("٦٩٠٠٥٠١٠")]
    public async Task InvalidCepIsRejectedBeforeAnyNetworkCall(string input)
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException()); using var query = Create(handler);
        await Assert.ThrowsAsync<DomainException>(() => query.PostalAddressAsync(input, default)); Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData("{\"erro\":true}")] [InlineData("{\"erro\":\"true\"}")]
    public async Task MissingCepIsNotTurnedIntoFakeAddress(string json)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply(json))); using var query = Create(handler);
        await Assert.ThrowsAsync<PublicDataNotFoundException>(() => query.PostalAddressAsync("00000000", default));
        Assert.Equal("available", query.Status().Single(x => x.Provider == "ViaCep").Status);
    }
    [Fact] public async Task MunicipalitySupportsNewIbgeRegionalHierarchy()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply(Municipality))); using var query = Create(handler);
        var result = await query.MunicipalityAsync(1302603, default); Assert.Equal("AM", result.Data.State);
    }
    [Fact] public async Task IbgeEmptyArrayMeansMissingMunicipality()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply("[]"))); using var query = Create(handler);
        await Assert.ThrowsAsync<PublicDataNotFoundException>(() => query.MunicipalityAsync(9999999, default));
    }
    [Fact] public async Task InvalidUfNeverReachesNetwork()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException()); using var query = Create(handler);
        await Assert.ThrowsAsync<DomainException>(() => query.MunicipalitiesAsync("AM/../../", default)); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task DisabledIntegrationCannotReturnPreviouslyCachedData()
    {
        var settings = new PublicDataSettings();
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply(Address))); using var query = Create(handler, settings);
        await query.PostalAddressAsync("69005010", default); settings.ViaCep.Enabled = false;
        Assert.Equal("disabled", (await Assert.ThrowsAsync<PublicApiException>(() => query.PostalAddressAsync("69005010", default))).Reason);
        Assert.Equal(1, handler.Calls); Assert.Equal("disabled", query.Status().Single(x => x.Provider == "ViaCep").Status);
    }
    [Fact] public async Task MalformedResponseIsNotCachedAndCanRecover()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply("<html>Gateway down</html>"))); using var query = Create(handler);
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<PublicApiException>(() => query.PostalAddressAsync("69005010", default))).Reason);
        Assert.Equal("unavailable", query.Status().Single(x => x.Provider == "ViaCep").Status);
        handler.Respond = (_, _) => Task.FromResult(Reply(Address));
        Assert.Equal("Manaus", (await query.PostalAddressAsync("69005010", default)).Data.City); Assert.Equal(2, handler.Calls);
        Assert.Null(query.Status().Single(x => x.Provider == "ViaCep").LastError);
    }
    [Fact] public async Task OversizedBodyIsRejected()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Reply(new string('x', 2 * 1024 * 1024 + 1)))); using var query = Create(handler);
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<PublicApiException>(() => query.PostalAddressAsync("69005010", default))).Reason);
    }
    [Fact] public async Task ProviderCapacityIsBoundedWithoutStartingAnUnboundedQueue()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, ct) => { await release.Task.WaitAsync(ct); return Reply(Address); });
        using var query = Create(handler);
        var one = query.PostalAddressAsync("69005010", default); var two = query.PostalAddressAsync("69005010", default);
        try
        {
            Assert.Equal("busy", (await Assert.ThrowsAsync<PublicApiException>(() => query.PostalAddressAsync("69005010", default))).Reason);
            Assert.Equal(2, handler.Calls);
        }
        finally { release.TrySetResult(); await Task.WhenAll(one, two); }
    }
    [Theory] [InlineData(429, "rate_limited")] [InlineData(500, "http_error")] [InlineData(302, "http_error")]
    public async Task UpstreamErrorsNeverBecomeSuccessfulResults(int status, string reason)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))); using var query = Create(handler);
        Assert.Equal(reason, (await Assert.ThrowsAsync<PublicApiException>(() => query.StatesAsync(default))).Reason);
        Assert.Equal("unavailable", query.Status().Single(x => x.Provider == "Ibge").Status);
    }
    [Fact] public async Task ProviderTimeoutIsDifferentFromCallerCancellation()
    {
        using var handler = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Reply(Address); });
        using var query = Create(handler, new PublicDataSettings { ViaCep = new() { TimeoutSeconds = 1 } });
        Assert.Equal("timeout", (await Assert.ThrowsAsync<PublicApiException>(() => query.PostalAddressAsync("69005010", default))).Reason);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.PostalAddressAsync("69005010", cancelled.Token));
        Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task PncpQueryUsesPublicationDatesAndPreservesMoney()
    {
        const string json = """{"data":[{"numeroControlePNCP":"12345678000190-2-000001/2026","numeroContratoEmpenho":"1","anoContrato":2026,"orgaoEntidade":{"cnpj":"12345678000190","razaoSocial":"Municipio de teste"},"objetoContrato":"Objeto publicado","valorInicial":123.45,"valorGlobal":246.90,"dataAssinatura":"2026-10-01"}],"totalRegistros":1,"totalPaginas":1,"numeroPagina":1}""";
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("https://pncp.gov.br/api/consulta/v1/contratos?dataInicial=20261001&dataFinal=20261002&pagina=1&tamanhoPagina=10&cnpjOrgao=12345678000190", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Reply(json));
        });
        using var query = Create(handler);
        var result = await query.ContractsAsync(new(2026, 10, 1), new(2026, 10, 2), 1, 10, "12345678000190", default);
        Assert.Equal(246.90m, result.Data.Items.Single().GlobalValue); Assert.Equal(new DateOnly(2026, 10, 1), result.Data.Items.Single().SignedOn);
    }
    [Fact] public async Task PncpNoContentIsExplicitEmptyPageButJsonNullIsInvalid()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))); using var query = Create(handler);
        var result = await query.ContractsAsync(new(2026, 10, 1), new(2026, 10, 1), 2, 10, null, default);
        Assert.Empty(result.Data.Items); Assert.Equal(2, result.Data.PageNumber);
        handler.Respond = (_, _) => Task.FromResult(Reply("null"));
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<PublicApiException>(() => query.ContractsAsync(new(2026, 10, 2), new(2026, 10, 2), 1, 10, null, default))).Reason);
    }
    [Fact] public async Task UnboundedPncpQueriesAreRejectedBeforeCallingProvider()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException()); using var query = Create(handler);
        await Assert.ThrowsAsync<DomainException>(() => query.ContractsAsync(new(2026, 1, 1), new(2026, 3, 1), 1, 10, null, default));
        await Assert.ThrowsAsync<DomainException>(() => query.ContractsAsync(new(2026, 1, 1), new(2026, 1, 1), 1, 500, null, default));
        await Assert.ThrowsAsync<DomainException>(() => query.ContractsAsync(new(2026, 1, 1), new(2026, 1, 1), 1, 10, "x&usuarioId=1", default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public void StatusDoesNotClaimVerificationBeforeAnyCall()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException()); using var query = Create(handler);
        Assert.All(query.Status(), status => { Assert.Equal("not_checked", status.Status); Assert.Null(status.LastSuccessAt); });
    }
    private static PublicDataGateway Create(StubHandler handler, PublicDataSettings? settings = null) => new(new StubFactory(handler), Options.Create(settings ?? new()));
    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class StubFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(name switch
        {
            "public-data-Ibge" => "https://servicodados.ibge.gov.br/api/v1/localidades/",
            "public-data-ViaCep" => "https://viacep.com.br/ws/",
            "public-data-Pncp" => "https://pncp.gov.br/api/consulta/v1/", _ => throw new InvalidOperationException()
        }) };
    }
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Respond(request, ct); }
    }
}
