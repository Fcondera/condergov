using CondertechGov.SharedKernel;
namespace CondertechGov.Api;

public static class PublicIntegrationEndpoints
{
    public static void MapPublicIntegrationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/integrations").WithTags("Public data integrations").RequireAuthorization().RequireRateLimiting("public-integrations");
        api.MapGet("/", (IPublicDataQueries queries) => TypedResults.Ok(queries.Status()));
        api.MapGet("/ibge/states", async (IPublicDataQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.StatesAsync(ct)));
        api.MapGet("/ibge/states/{uf}/municipalities", async (string uf, IPublicDataQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.MunicipalitiesAsync(uf, ct)));
        api.MapGet("/ibge/municipalities/{ibgeCode:int}", async (int ibgeCode, IPublicDataQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.MunicipalityAsync(ibgeCode, ct)));
        api.MapGet("/viacep/addresses/{cep}", async (string cep, IPublicDataQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.PostalAddressAsync(cep, ct)));
        api.MapGet("/pncp/contracts", async (DateOnly publishedFrom, DateOnly publishedTo, IPublicDataQueries queries, CancellationToken ct,
            int page = 1, int pageSize = 10, string? organizationCnpj = null) => TypedResults.Ok(await queries.ContractsAsync(publishedFrom, publishedTo, page, pageSize, organizationCnpj, ct)));
    }
}
