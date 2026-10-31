using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using CondertechGov.SharedKernel;
using Microsoft.Extensions.Options;
namespace CondertechGov.Infrastructure;

public sealed class StorageSettings
{
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "gov-private";
}
public sealed class S3PrivateStorage(IOptions<StorageSettings> settings) : IPrivateStorage, IDisposable
{
    private AmazonS3Client? client;
    private AmazonS3Client Client => client ??= new(settings.Value.AccessKey, settings.Value.SecretKey,
        new AmazonS3Config { ServiceURL = settings.Value.Endpoint, ForcePathStyle = true });
    public async Task PutAsync(string key, Stream stream, string contentType, CancellationToken ct) =>
        await Client.PutObjectAsync(new PutObjectRequest { BucketName = settings.Value.Bucket, Key = key,
            InputStream = stream, ContentType = contentType, AutoCloseStream = false }, ct);
    public async Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        var response = await Client.GetObjectAsync(settings.Value.Bucket, key, ct);
        return new OwnedResponseStream(response);
    }
    public void Dispose() => client?.Dispose();
    private sealed class OwnedResponseStream(GetObjectResponse response) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => response.ContentLength;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => response.ResponseStream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => response.ResponseStream.ReadAsync(buffer, ct);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) response.Dispose(); base.Dispose(disposing); }
    }
}
public sealed class ScannerSettings { public string Host { get; set; } = "clamav"; public int Port { get; set; } = 3310; }
public sealed class ClamAvScanner(IOptions<ScannerSettings> settings) : IMalwareScanner
{
    public async Task<bool> IsCleanAsync(Stream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var client = new TcpClient();
        await client.ConnectAsync(settings.Value.Host, settings.Value.Port, timeout.Token);
        await using var network = client.GetStream();
        await network.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), timeout.Token);
        var buffer = new byte[65536]; var header = new byte[4]; int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(header, read);
            await network.WriteAsync(header, timeout.Token);
            await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }
        await network.WriteAsync(new byte[4], timeout.Token);
        using var reply = new MemoryStream();
        do
        {
            read = await network.ReadAsync(buffer, timeout.Token);
            if (read > 0) reply.Write(buffer, 0, read);
        } while (read > 0 && reply.Length < 4096 && !buffer.AsSpan(0, read).Contains((byte)0));
        var result = Encoding.UTF8.GetString(reply.ToArray()).TrimEnd('\0', '\n');
        if (result.EndsWith(" FOUND", StringComparison.Ordinal)) return false;
        if (result == "stream: OK") return true;
        throw new IOException("Scanner não confirmou análise do arquivo.");
    }
}
