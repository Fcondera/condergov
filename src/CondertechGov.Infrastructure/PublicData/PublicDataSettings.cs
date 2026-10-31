using CondertechGov.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
namespace CondertechGov.Infrastructure.PublicData;

public sealed class PublicProviderSettings
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 10;
}
public sealed class PublicDataSettings
{
    public PublicProviderSettings Ibge { get; set; } = new();
    public PublicProviderSettings ViaCep { get; set; } = new();
    public PublicProviderSettings Pncp { get; set; } = new();
    public PublicProviderSettings For(PublicProvider provider) => provider switch
    { PublicProvider.Ibge => Ibge, PublicProvider.ViaCep => ViaCep, PublicProvider.Pncp => Pncp, _ => throw new ArgumentOutOfRangeException(nameof(provider)) };
}
public static class PublicDataRegistration
{
    public static IServiceCollection AddPublicDataIntegrations(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PublicDataSettings>().Bind(config.GetSection("PublicIntegrations"))
            .Validate(s => Enum.GetValues<PublicProvider>().All(p => s.For(p).TimeoutSeconds is >= 1 and <= 30), "Timeout deve estar entre 1 e 30 segundos.")
            .ValidateOnStart();
        AddClient(services, PublicProvider.Ibge, "https://servicodados.ibge.gov.br/api/v1/localidades/");
        AddClient(services, PublicProvider.ViaCep, "https://viacep.com.br/ws/");
        AddClient(services, PublicProvider.Pncp, "https://pncp.gov.br/api/consulta/v1/");
        services.AddSingleton<PublicDataGateway>();
        services.AddSingleton<IPublicDataQueries>(sp => sp.GetRequiredService<PublicDataGateway>());
        return services;
    }
    private static void AddClient(IServiceCollection services, PublicProvider provider, string url) =>
        services.AddHttpClient("public-data-" + provider, c =>
        {
            c.BaseAddress = new Uri(url); c.Timeout = Timeout.InfiniteTimeSpan;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("CondertechGov/1.0");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
}
