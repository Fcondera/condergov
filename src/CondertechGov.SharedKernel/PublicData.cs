namespace CondertechGov.SharedKernel;

public enum PublicProvider { Ibge, ViaCep, Pncp }
public sealed record ExternalData<T>(string Provider, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt, bool FromCache, T Data);
public sealed record BrazilianState(int IbgeCode, string Abbreviation, string Name);
public sealed record BrazilianMunicipality(int IbgeCode, string Name, string State);
public sealed record PostalAddress(string Cep, string Street, string Complement, string Neighborhood, string City, string State, string? IbgeCode);
public sealed record PublicContract(string ControlNumber, string? Number, int Year, string OrganizationCnpj,
    string OrganizationName, string? Object, decimal? InitialValue, decimal? GlobalValue,
    DateOnly? SignedOn, DateOnly? StartsOn, DateOnly? EndsOn);
public sealed record PublicContractPage(IReadOnlyList<PublicContract> Items, long Total, long TotalPages, int PageNumber, int PageSize);
public sealed record PublicIntegrationStatus(string Provider, bool Enabled, string Status, DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt, string? LastError);
public sealed class PublicApiException(PublicProvider provider, string reason) : Exception("Falha na consulta externa.")
{
    public PublicProvider Provider { get; } = provider;
    public string Reason { get; } = reason;
}
public sealed class PublicDataNotFoundException : Exception;
public interface IPublicDataQueries
{
    Task<ExternalData<IReadOnlyList<BrazilianState>>> StatesAsync(CancellationToken ct);
    Task<ExternalData<IReadOnlyList<BrazilianMunicipality>>> MunicipalitiesAsync(string uf, CancellationToken ct);
    Task<ExternalData<BrazilianMunicipality>> MunicipalityAsync(int ibgeCode, CancellationToken ct);
    Task<ExternalData<PostalAddress>> PostalAddressAsync(string cep, CancellationToken ct);
    Task<ExternalData<PublicContractPage>> ContractsAsync(DateOnly publishedFrom, DateOnly publishedTo, int page, int pageSize, string? organizationCnpj, CancellationToken ct);
    IReadOnlyList<PublicIntegrationStatus> Status();
}
