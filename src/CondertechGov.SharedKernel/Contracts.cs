namespace CondertechGov.SharedKernel;

public interface ITenantContext { Guid TenantId { get; } Guid? UserId { get; } string CorrelationId { get; } }
public abstract class TenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
}
public sealed class DomainException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public interface IPrivateStorage
{
    Task PutAsync(string key, Stream stream, string contentType, CancellationToken ct);
    Task<Stream> OpenAsync(string key, CancellationToken ct);
}
public interface IMalwareScanner { Task<bool> IsCleanAsync(Stream stream, CancellationToken ct); }
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);
public static class Permissions
{
    public const string UnitsRead = "units.read";
    public const string UnitsWrite = "units.write";
    public const string UsersManage = "users.manage";
    public const string AuditRead = "audit.read";
    public const string FilesRead = "files.read";
    public const string FilesWrite = "files.write";
    public static readonly string[] All = [UnitsRead, UnitsWrite, UsersManage, AuditRead, FilesRead, FilesWrite];
}
