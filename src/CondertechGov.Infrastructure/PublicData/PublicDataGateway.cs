using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CondertechGov.SharedKernel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace CondertechGov.Infrastructure.PublicData;

public sealed class PublicDataGateway(IHttpClientFactory clients, IOptions<PublicDataSettings> options) : IPublicDataQueries, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> States = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    private readonly ConcurrentDictionary<PublicProvider, PublicIntegrationStatus> observations = new();
    private readonly Dictionary<PublicProvider, SemaphoreSlim> gates = Enum.GetValues<PublicProvider>().ToDictionary(p => p, _ => new SemaphoreSlim(2, 2));

    public Task<ExternalData<IReadOnlyList<BrazilianState>>> StatesAsync(CancellationToken ct) =>
        Query<IReadOnlyList<BrazilianState>>(PublicProvider.Ibge, "estados?orderBy=nome", TimeSpan.FromDays(1), root =>
        {
            var rows = Read<StateWire[]>(root);
            if (rows.Length == 0 || rows.Any(x => x.Id <= 0 || !States.Contains(x.Sigla ?? "") || string.IsNullOrWhiteSpace(x.Nome))) throw new JsonException();
            return rows.Select(x => new BrazilianState(x.Id, x.Sigla!, x.Nome!)).ToArray();
        }, ct);

    public Task<ExternalData<IReadOnlyList<BrazilianMunicipality>>> MunicipalitiesAsync(string uf, CancellationToken ct)
    {
        uf = ValidateState(uf);
        return Query<IReadOnlyList<BrazilianMunicipality>>(PublicProvider.Ibge, $"estados/{uf}/municipios?orderBy=nome", TimeSpan.FromDays(1), root =>
        {
            var rows = Read<MunicipalityWire[]>(root).Select(ToMunicipality).ToArray();
            if (rows.Any(x => x.State != uf)) throw new JsonException();
            return rows;
        }, ct);
    }
    public Task<ExternalData<BrazilianMunicipality>> MunicipalityAsync(int ibgeCode, CancellationToken ct)
    {
        if (ibgeCode is < 1000000 or > 9999999) throw new DomainException("Código IBGE deve ter sete dígitos.");
        return Query(PublicProvider.Ibge, $"municipios/{ibgeCode}", TimeSpan.FromDays(1), root =>
        {
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0) throw new PublicDataNotFoundException();
            var row = ToMunicipality(Read<MunicipalityWire>(root));
            if (row.IbgeCode != ibgeCode) throw new JsonException(); return row;
        }, ct);
    }
    public Task<ExternalData<PostalAddress>> PostalAddressAsync(string cep, CancellationToken ct)
    {
        if (cep is null || !Regex.IsMatch(cep, "^[0-9]{5}-?[0-9]{3}$", RegexOptions.CultureInvariant)) throw new DomainException("CEP deve conter oito dígitos, com hífen opcional.");
        var digits = cep.Replace("-", "", StringComparison.Ordinal);
        return Query(PublicProvider.ViaCep, $"{digits}/json/", TimeSpan.FromDays(1), root =>
        {
            if (root.TryGetProperty("erro", out var error) && (error.ValueKind == JsonValueKind.True ||
                error.ValueKind == JsonValueKind.String && error.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)) throw new PublicDataNotFoundException();
            var row = Read<AddressWire>(root);
            if (row.Cep?.Replace("-", "", StringComparison.Ordinal) != digits || string.IsNullOrWhiteSpace(row.Localidade) || !States.Contains(row.Uf ?? "")) throw new JsonException();
            return new PostalAddress(row.Cep!, row.Logradouro ?? "", row.Complemento ?? "", row.Bairro ?? "", row.Localidade, row.Uf!, row.Ibge);
        }, ct);
    }
    public Task<ExternalData<PublicContractPage>> ContractsAsync(DateOnly publishedFrom, DateOnly publishedTo, int page, int pageSize, string? organizationCnpj, CancellationToken ct)
    {
        if (publishedFrom.Year < 2021 || publishedTo < publishedFrom || publishedTo.DayNumber - publishedFrom.DayNumber > 30)
            throw new DomainException("Informe um período de publicação de até 31 dias, a partir de 2021.");
        if (page is < 1 or > 10000 || pageSize is < 10 or > 50) throw new DomainException("Página deve estar entre 1 e 10000; tamanho entre 10 e 50.");
        organizationCnpj = organizationCnpj?.ToUpperInvariant();
        if (organizationCnpj is not null && !Regex.IsMatch(organizationCnpj, "^[A-Z0-9]{12}[0-9]{2}$", RegexOptions.CultureInvariant))
            throw new DomainException("CNPJ do órgão deve ter 14 posições, sem pontuação.");
        var path = $"contratos?dataInicial={publishedFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&dataFinal={publishedTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&pagina={page}&tamanhoPagina={pageSize}";
        if (organizationCnpj is not null) path += "&cnpjOrgao=" + Uri.EscapeDataString(organizationCnpj);
        return Query(PublicProvider.Pncp, path, TimeSpan.FromMinutes(5), root =>
        {
            var result = Read<ContractPageWire>(root);
            if (result.Data is null || result.TotalRegistros is null or < 0 || result.TotalPaginas is null or < 0 || result.NumeroPagina != page ||
                result.Data.Length > pageSize || result.TotalRegistros < result.Data.Length) throw new JsonException();
            var items = result.Data.Select(x =>
            {
                if (string.IsNullOrWhiteSpace(x.NumeroControlePNCP) || string.IsNullOrWhiteSpace(x.OrgaoEntidade?.Cnpj) || string.IsNullOrWhiteSpace(x.OrgaoEntidade.RazaoSocial) || x.AnoContrato <= 0) throw new JsonException();
                return new PublicContract(x.NumeroControlePNCP, x.NumeroContratoEmpenho, x.AnoContrato, x.OrgaoEntidade.Cnpj, x.OrgaoEntidade.RazaoSocial,
                    x.ObjetoContrato, x.ValorInicial, x.ValorGlobal, x.DataAssinatura, x.DataVigenciaInicio, x.DataVigenciaFim);
            }).ToArray();
            return new PublicContractPage(items, result.TotalRegistros.Value, result.TotalPaginas.Value, page, pageSize);
        }, ct, () => new PublicContractPage([], 0, 0, page, pageSize));
    }

    public IReadOnlyList<PublicIntegrationStatus> Status() => Enum.GetValues<PublicProvider>().Select(p =>
    {
        var enabled = options.Value.For(p).Enabled;
        var state = observations.GetValueOrDefault(p) ?? new PublicIntegrationStatus(p.ToString(), enabled, "not_checked", null, null, null);
        return state with { Enabled = enabled, Status = !enabled ? "disabled" : state.LastAttemptAt < DateTimeOffset.UtcNow.AddMinutes(-5) ? "stale" : state.Status };
    }).ToArray();

    private async Task<ExternalData<T>> Query<T>(PublicProvider provider, string path, TimeSpan ttl, Func<JsonElement, T> parse, CancellationToken ct, Func<T>? emptyResponse = null)
    {
        ct.ThrowIfCancellationRequested();
        var settings = options.Value.For(provider);
        if (!settings.Enabled) throw new PublicApiException(provider, "disabled");
        var key = provider + ":" + path;
        if (cache.TryGetValue<ExternalData<T>>(key, out var cached)) return cached! with { FromCache = true };
        var gate = gates[provider];
        if (!await gate.WaitAsync(0, ct)) throw new PublicApiException(provider, "busy");
        try
        {
            if (cache.TryGetValue<ExternalData<T>>(key, out cached)) return cached! with { FromCache = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            try
            {
                using var client = clients.CreateClient("public-data-" + provider);
                using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound && (provider == PublicProvider.ViaCep ||
                    provider == PublicProvider.Ibge && path.StartsWith("municipios/", StringComparison.Ordinal))) throw new PublicDataNotFoundException();
                if (!response.IsSuccessStatusCode) throw new PublicApiException(provider, response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "http_error");
                T data;
                if (response.StatusCode == HttpStatusCode.NoContent && provider == PublicProvider.Pncp && emptyResponse is not null)
                    data = emptyResponse();
                else
                {
                    if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength > MaxResponseBytes) throw new PublicApiException(provider, "invalid_response");
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var buffer = new MemoryStream(); var chunk = new byte[16384]; int read;
                    while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
                    {
                        if (buffer.Length + read > MaxResponseBytes) throw new PublicApiException(provider, "invalid_response");
                        buffer.Write(chunk, 0, read);
                    }
                    using var doc = JsonDocument.Parse(buffer.ToArray()); data = parse(doc.RootElement);
                }
                var now = DateTimeOffset.UtcNow;
                observations[provider] = new(provider.ToString(), true, "available", now, now, null);
                var result = new ExternalData<T>(provider.ToString(), now, now.Add(ttl), false, data);
                cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpiration = result.ExpiresAt, Size = JsonSerializer.SerializeToUtf8Bytes(result, Json).LongLength });
                return result;
            }
            catch (PublicDataNotFoundException) { Observe(provider, null); throw; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Observe(provider, "timeout"); throw new PublicApiException(provider, "timeout"); }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or PublicApiException)
            {
                var reason = e is PublicApiException external ? external.Reason : e is JsonException or InvalidOperationException ? "invalid_response" : "network_error";
                Observe(provider, reason); throw new PublicApiException(provider, reason);
            }
        }
        finally { gate.Release(); }
    }
    private void Observe(PublicProvider provider, string? reason)
    {
        var now = DateTimeOffset.UtcNow;
        observations.AddOrUpdate(provider, _ => new(provider.ToString(), true, reason is null ? "available" : "unavailable", now, reason is null ? now : null, reason),
            (_, old) => old with { Status = reason is null ? "available" : "unavailable", LastAttemptAt = now, LastSuccessAt = reason is null ? now : old.LastSuccessAt, LastError = reason });
    }
    private static T Read<T>(JsonElement root) => root.Deserialize<T>(Json) ?? throw new JsonException();
    private static string ValidateState(string uf)
    {
        uf = uf?.ToUpperInvariant() ?? "";
        if (!States.Contains(uf)) throw new DomainException("UF inválida."); return uf;
    }
    private static BrazilianMunicipality ToMunicipality(MunicipalityWire row)
    {
        var uf = row.Microrregiao?.Mesorregiao?.Uf?.Sigla ?? row.ImmediateRegion?.IntermediateRegion?.Uf?.Sigla;
        if (row.Id is < 1000000 or > 9999999 || string.IsNullOrWhiteSpace(row.Nome) || !States.Contains(uf ?? "")) throw new JsonException();
        return new(row.Id, row.Nome, uf!);
    }
    public void Dispose() { cache.Dispose(); foreach (var gate in gates.Values) gate.Dispose(); }

    private sealed record StateWire(int Id, string? Sigla, string? Nome);
    private sealed record MunicipalityWire(int Id, string? Nome, MicroregionWire? Microrregiao,
        [property: JsonPropertyName("regiao-imediata")] ImmediateRegionWire? ImmediateRegion);
    private sealed record MicroregionWire(MesoregionWire? Mesorregiao);
    private sealed record MesoregionWire(StateWire? Uf);
    private sealed record ImmediateRegionWire([property: JsonPropertyName("regiao-intermediaria")] IntermediateRegionWire? IntermediateRegion);
    private sealed record IntermediateRegionWire(StateWire? Uf);
    private sealed record AddressWire(string? Cep, string? Logradouro, string? Complemento, string? Bairro, string? Localidade, string? Uf, string? Ibge);
    private sealed record ContractPageWire(ContractWire[]? Data, long? TotalRegistros, long? TotalPaginas, int? NumeroPagina);
    private sealed record OrganizationWire(string? Cnpj, string? RazaoSocial);
    private sealed record ContractWire(string? NumeroControlePNCP, string? NumeroContratoEmpenho, int AnoContrato, OrganizationWire? OrgaoEntidade,
        string? ObjetoContrato, decimal? ValorInicial, decimal? ValorGlobal, DateOnly? DataAssinatura, DateOnly? DataVigenciaInicio, DateOnly? DataVigenciaFim);
}
