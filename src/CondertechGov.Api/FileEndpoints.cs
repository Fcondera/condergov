using System.Data;
using System.Security.Cryptography;
using CondertechGov.Infrastructure;
using CondertechGov.SharedKernel;
using Microsoft.EntityFrameworkCore;
namespace CondertechGov.Api;
public static class FileEndpoints
{
    public static void MapFileEndpoints(this WebApplication app)
    {
        var files = app.MapGroup("/api/v1/files").WithTags("Private files");
        files.MapPost("/", async (HttpRequest request, TenantScope scope, GovDbContext db, IPrivateStorage storage, CancellationToken ct) =>
        {
            var name = request.Headers["X-File-Name"].ToString();
            if (name.Length is < 1 or > 200 || name.IndexOfAny(['/', '\\', '\r', '\n']) >= 0 ||
                request.ContentLength is null or <= 0 or > 10_485_760) return Results.BadRequest();
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length != request.ContentLength) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var quota = await db.Tenants.Where(x => x.Id == scope.TenantId).Select(x => x.StorageLimitBytes).SingleAsync(ct);
            var used = await db.Files.SumAsync(x => x.Size, ct);
            if (used + buffer.Length > quota) return Results.Problem(statusCode: 409, title: "Cota de armazenamento atingida.");
            var file = new StoredFile { TenantId = scope.TenantId, Name = name, Size = buffer.Length,
                ObjectKey = $"{scope.TenantId:N}/{Guid.NewGuid():N}", Sha256 = Convert.ToHexString(SHA256.HashData(buffer.ToArray())) };
            buffer.Position = 0;
            await storage.PutAsync(file.ObjectKey, buffer, file.ContentType, ct);
            db.Files.Add(file); db.Record("file.upload", file.Id.ToString()); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/files/{file.Id}", new { file.Id, file.Name, file.Size, file.Sha256, status = "quarantined" });
        }).RequireAuthorization(Permissions.FilesWrite);
        files.MapGet("/{id:guid}", async (Guid id, GovDbContext db, CancellationToken ct) =>
        {
            var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return file is null ? Results.NotFound() : Results.Ok(new { file.Id, file.Name, file.Size, file.Sha256,
                status = file.Clean ? "clean" : file.Rejected ? "rejected" : "quarantined" });
        }).RequireAuthorization(Permissions.FilesRead);
        files.MapGet("/{id:guid}/download", async (Guid id, TenantScope scope, GovDbContext db, IPrivateStorage storage, CancellationToken ct) =>
        {
            var file = await db.Files.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (file is null) return Results.NotFound();
            if (!file.Clean) return Results.Problem(statusCode: 409, title: "Arquivo não liberado pelo scanner.");
            if (!file.ObjectKey.StartsWith($"{scope.TenantId:N}/", StringComparison.Ordinal)) return Results.NotFound();
            db.Record("file.download", id.ToString()); await db.SaveChangesAsync(ct);
            var stream = await storage.OpenAsync(file.ObjectKey, ct);
            return Results.File(stream, "application/octet-stream", file.Name, enableRangeProcessing: false);
        }).RequireAuthorization(Permissions.FilesRead);
    }
}
