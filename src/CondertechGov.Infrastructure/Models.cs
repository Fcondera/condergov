using CondertechGov.SharedKernel;
namespace CondertechGov.Infrastructure;
public sealed class AuditEvent : TenantEntity
{
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class StoredFile : TenantEntity
{
    public string ObjectKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Clean { get; set; }
    public bool Rejected { get; set; }
    public int ScanAttempts { get; set; }
}
public sealed class Notification : TenantEntity
{
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
