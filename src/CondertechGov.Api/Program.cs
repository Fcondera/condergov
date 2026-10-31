using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using CondertechGov.Api;
using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Host.UseSerilog((_, c) => c.WriteTo.Console());
builder.Services.AddGovInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["correlationId"] = c.HttpContext.TraceIdentifier;
    c.HttpContext.Response.Headers["X-Correlation-ID"] = c.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<GovDbContext>();
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("CondertechGov.Api"))
    .WithTracing(t => { t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation(); if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) t.AddOtlpExporter(); });
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new();
var adminKey = builder.Configuration["Platform:ProvisioningKey"] ?? "";
if (Encoding.UTF8.GetByteCount(jwt.Key) < 64 || adminKey.Length < 32)
    throw new InvalidOperationException("Configure Jwt:Key (64 bytes ou mais) e Platform:ProvisioningKey (32 caracteres ou mais) por secrets/ambiente.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15), ValidAlgorithms = [SecurityAlgorithms.HmacSha512]
    };
    o.Events = new JwtBearerEvents { OnTokenValidated = async c =>
    {
        if (!Guid.TryParse(c.Principal?.FindFirstValue("tenant"), out var tid) || tid == Guid.Empty ||
            !Guid.TryParse(c.Principal?.FindFirstValue("sub"), out var uid) ||
            !Guid.TryParse(c.Principal?.FindFirstValue("sid"), out var sid)) { c.Fail("Token inválido."); return; }
        c.HttpContext.RequestServices.GetRequiredService<TenantScope>().Resolve(tid, uid);
        var db = c.HttpContext.RequestServices.GetRequiredService<GovDbContext>();
        var ct = c.HttpContext.RequestAborted;
        var stamp = c.Principal!.FindFirstValue("stamp");
        if (!await db.Users.AnyAsync(u => u.Id == uid && !u.Suspended && u.SecurityStamp == stamp, ct) ||
            !await db.Sessions.AnyAsync(s => s.Id == sid && s.UserId == uid && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow, ct)) c.Fail("Sessão revogada.");
    }};
});
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    foreach (var p in Permissions.All) o.AddPolicy(p, policy => policy.RequireClaim("permission", p));
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("authentication", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = builder.Configuration.GetValue("RateLimit:AuthenticationPermitLimit", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("public-integrations", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Correlation-ID"] = ctx.TraceIdentifier;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.RequestServices.GetRequiredService<TenantScope>().CorrelationId = ctx.TraceIdentifier;
    await next();
});
app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
        !ctx.Request.Path.StartsWithSegments("/api/v1/auth"))
    {
        var scope = ctx.RequestServices.GetRequiredService<TenantScope>();
        var tenant = await ctx.RequestServices.GetRequiredService<GovDbContext>().Tenants.AsNoTracking().SingleAsync(x => x.Id == scope.TenantId, ctx.RequestAborted);
        if (!tenant.WriteEnabled || tenant.SubscriptionExpiresAt <= DateTimeOffset.UtcNow)
        { await Results.Problem(statusCode: 403, title: "Órgão disponível para consulta. Contate a administração para novas alterações.").ExecuteAsync(ctx); return; }
    }
    await next();
});
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapGovEndpoints(adminKey);
app.MapPublicIntegrationEndpoints();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GovDbContext>().Database.MigrateAsync();
    return;
}
await app.RunAsync();
public partial class Program;
