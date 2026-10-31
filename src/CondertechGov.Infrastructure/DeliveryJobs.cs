using System.Net;
using System.Net.Mail;
using CondertechGov.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CondertechGov.Infrastructure;
public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}
public sealed class DeliveryJobs(IServiceScopeFactory scopes, ILogger<DeliveryJobs> logger)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task DispatchAsync(CancellationToken ct)
    {
        // Only the control-plane directory is read without a tenant. Content is always scoped below.
        using var directory = scopes.CreateScope();
        var ids = await directory.ServiceProvider.GetRequiredService<GovDbContext>().Tenants.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
        foreach (var tenantId in ids)
        {
            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<TenantScope>().Resolve(tenantId);
            var db = sp.GetRequiredService<GovDbContext>();
            var pending = await db.Notifications.Where(x => x.SentAt == null && x.Attempts < 10).OrderBy(x => x.Id).Take(25).ToListAsync(ct);
            var smtp = sp.GetRequiredService<IOptions<SmtpSettings>>().Value;
            foreach (var notification in pending)
            {
                notification.Attempts++;
                await db.SaveChangesAsync(ct);
                try
                {
                    if (string.IsNullOrWhiteSpace(smtp.Host)) throw new InvalidOperationException("SMTP não configurado.");
                    using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
                    if (!string.IsNullOrEmpty(smtp.Username)) client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
                    using var message = new MailMessage(smtp.From, notification.Recipient, notification.Subject, notification.Body);
                    await client.SendMailAsync(message, ct);
                    notification.SentAt = DateTimeOffset.UtcNow; notification.LastError = null;
                    db.Record("notification.sent", notification.Id.ToString());
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    notification.LastError = e.GetType().Name;
                    logger.LogWarning("Falha de notificação {Notification} no tenant {Tenant}: {Error}", notification.Id, tenantId, e.GetType().Name);
                }
                await db.SaveChangesAsync(ct);
            }
            var files = await db.Files.Where(x => !x.Clean && !x.Rejected && x.ScanAttempts < 10).OrderBy(x => x.Id).Take(25).ToListAsync(ct);
            foreach (var file in files)
            {
                file.ScanAttempts++;
                await db.SaveChangesAsync(ct);
                try
                {
                    if (!file.ObjectKey.StartsWith($"{tenantId:N}/", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
                    await using var stream = await sp.GetRequiredService<IPrivateStorage>().OpenAsync(file.ObjectKey, ct);
                    if (await sp.GetRequiredService<IMalwareScanner>().IsCleanAsync(stream, ct))
                    {
                        file.Clean = true; db.Record("file.scan.clean", file.Id.ToString()); await db.SaveChangesAsync(ct);
                    }
                    else { file.Rejected = true; db.Record("file.scan.rejected", file.Id.ToString()); await db.SaveChangesAsync(ct); }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { logger.LogWarning("Falha de análise {File} no tenant {Tenant}: {Error}", file.Id, tenantId, e.GetType().Name); }
            }
        }
    }
}
