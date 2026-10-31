using System.Globalization;
using System.Text.Json;
using CondertechGov.Infrastructure.PublicData;
using CondertechGov.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
if (args.Length > 1 || args.Length == 1 && !DateOnly.TryParseExact(args[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
{ Console.Error.WriteLine("Uso: dotnet run --project tools/CondertechGov.PublicApiProbe -- [yyyy-MM-dd]"); return 2; }
var services = new ServiceCollection();
services.AddLogging(); services.AddPublicDataIntegrations(new ConfigurationBuilder().AddEnvironmentVariables().Build());
await using var provider = services.BuildServiceProvider();
var queries = provider.GetRequiredService<IPublicDataQueries>();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var failures = 0;
await Check("IBGE UFs", async () => $"{(await queries.StatesAsync(timeout.Token)).Data.Count} UFs");
await Check("IBGE municípios AM", async () => $"{(await queries.MunicipalitiesAsync("AM", timeout.Token)).Data.Count} municípios");
await Check("IBGE Manaus", async () => (await queries.MunicipalityAsync(1302603, timeout.Token)).Data.Name);
await Check("ViaCEP", async () => (await queries.PostalAddressAsync("69005010", timeout.Token)).Data.City);
await Check($"PNCP publicação {day:yyyy-MM-dd}", async () => $"{(await queries.ContractsAsync(day, day, 1, 10, null, timeout.Token)).Data.Items.Count} contratos na página");
Console.WriteLine(JsonSerializer.Serialize(queries.Status(), new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

async Task Check(string name, Func<Task<string>> run)
{
    try { Console.WriteLine($"{name}: OK — {await run()}"); }
    catch (PublicApiException e) { failures++; Console.WriteLine($"{name}: FALHOU — {e.Provider}/{e.Reason}"); }
    catch (PublicDataNotFoundException) { failures++; Console.WriteLine($"{name}: FALHOU — referência não encontrada no provedor"); }
    catch (DomainException e) { failures++; Console.WriteLine($"{name}: PARÂMETRO INVÁLIDO — {e.Message}"); }
    catch (OperationCanceledException) { failures++; Console.WriteLine($"{name}: CANCELADO"); }
}
