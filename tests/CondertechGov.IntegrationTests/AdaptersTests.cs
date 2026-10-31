using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace CondertechGov.IntegrationTests;
[Collection("api")]
public sealed class AdaptersTests(ApiFixture f)
{
    [Fact] public async Task RealS3UploadPersistsBytesPrivatelyAndKeepsQuarantine()
    {
        var bytes = Encoding.UTF8.GetBytes("Arquivo institucional de teste S3");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-File-Name", "documento.txt");
        var response = await f.A.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = f.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantA);
        var file = await scope.ServiceProvider.GetRequiredService<GovDbContext>().Files.AsNoTracking().SingleAsync(x => x.Id == id);
        await using var stream = await scope.ServiceProvider.GetRequiredService<IPrivateStorage>().OpenAsync(file.ObjectKey, CancellationToken.None);
        using var actual = new MemoryStream(); await stream.CopyToAsync(actual); Assert.Equal(bytes, actual.ToArray());
        using var anonymous = new HttpClient();
        Assert.False((await anonymous.GetAsync($"{f.StorageEndpoint}/gov-private/{file.ObjectKey}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.A.GetAsync($"/api/v1/files/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.GetAsync($"/api/v1/files/{id}/download")).StatusCode);
    }
    [Fact] public async Task NotificationWorkerDeliversViaRealSmtpAndMarksOutbox()
    {
        Guid id;
        using (var scope = f.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantB);
            var db = scope.ServiceProvider.GetRequiredService<GovDbContext>();
            var notification = new Notification { TenantId = f.TenantB, Recipient = "smtp-test@example.gov.br", Subject = "Teste de entrega", Body = "Notificação institucional" };
            db.Notifications.Add(notification); await db.SaveChangesAsync(); id = notification.Id;
        }
        await f.Factory.Services.GetRequiredService<DeliveryJobs>().DispatchAsync(CancellationToken.None);
        using var verify = f.Factory.Services.CreateScope(); verify.ServiceProvider.GetRequiredService<TenantScope>().Resolve(f.TenantB);
        var sent = await verify.ServiceProvider.GetRequiredService<GovDbContext>().Notifications.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.NotNull(sent.SentAt); Assert.Equal(1, sent.Attempts);
        using var mail = new HttpClient(); var messages = await mail.GetStringAsync($"{f.MailEndpoint}/api/v1/messages");
        Assert.Contains("smtp-test@example.gov.br", messages);
        await f.Factory.Services.GetRequiredService<DeliveryJobs>().DispatchAsync(CancellationToken.None);
        Assert.Equal(messages, await mail.GetStringAsync($"{f.MailEndpoint}/api/v1/messages"));
    }
}
