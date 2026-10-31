using CondertechGov.Infrastructure;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(c => c.WriteTo.Console());
builder.Services.AddGovInfrastructure(builder.Configuration);
builder.Services.AddHangfireServer(o => o.WorkerCount = 1);
var host = builder.Build();
host.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<DeliveryJobs>("tenant-deliveries",
    job => job.DispatchAsync(CancellationToken.None), Cron.Minutely());
await host.RunAsync();
