using CondertechGov.Identity;
using CondertechGov.SharedKernel;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CondertechGov.Infrastructure.PublicData;
namespace CondertechGov.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddGovInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres ausente.");
        services.AddScoped<TenantScope>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantScope>());
        services.AddDbContext<GovDbContext>(o => o.UseNpgsql(connection));
        services.AddIdentityCore<GovUser>(o =>
        {
            o.Password.RequiredLength = 12; o.Password.RequireDigit = true; o.Password.RequireUppercase = true;
            o.Password.RequireLowercase = true; o.Password.RequireNonAlphanumeric = true;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.Lockout.MaxFailedAccessAttempts = 5;
            o.User.AllowedUserNameCharacters = "";
        }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<GovDbContext>().AddSignInManager().AddDefaultTokenProviders();
        services.AddDataProtection();
        services.AddPublicDataIntegrations(config);
        services.Configure<JwtSettings>(config.GetSection("Jwt"));
        services.Configure<StorageSettings>(config.GetSection("Storage"));
        services.Configure<ScannerSettings>(config.GetSection("Scanner"));
        services.Configure<SmtpSettings>(config.GetSection("Smtp"));
        services.AddScoped<AuthenticationService>(); services.AddScoped<ProvisioningService>(); services.AddScoped<OrganizationService>();
        services.AddValidatorsFromAssemblyContaining<ProvisionTenantValidator>();
        services.AddScoped<IPrivateStorage, S3PrivateStorage>(); services.AddScoped<IMalwareScanner, ClamAvScanner>();
        services.AddSingleton<DeliveryJobs>();
        services.AddHangfire(c => c.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connection)));
        if (!string.IsNullOrEmpty(config.GetConnectionString("Redis")))
            services.AddStackExchangeRedisCache(o => { o.Configuration = config.GetConnectionString("Redis"); o.InstanceName = "condertech:"; });
        return services;
    }
}
