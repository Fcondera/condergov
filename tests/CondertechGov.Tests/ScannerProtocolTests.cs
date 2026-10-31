using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CondertechGov.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;
namespace CondertechGov.Tests;
public sealed class ScannerProtocolTests
{
    [Theory]
    [InlineData("stream: OK\0", true)]
    [InlineData("stream: Eicar-Signature FOUND\0", false)]
    public async Task UsesLengthPrefixedClamAvProtocol(string response, bool expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Encoding.UTF8.GetBytes("Documento para análise de protocolo");
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(); await using var stream = peer.GetStream();
            var command = new byte[10]; await stream.ReadExactlyAsync(command);
            Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));
            using var received = new MemoryStream(); var header = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(header); var length = BinaryPrimitives.ReadInt32BigEndian(header); if (length == 0) break;
                var data = new byte[length]; await stream.ReadExactlyAsync(data); await received.WriteAsync(data);
            }
            Assert.Equal(payload, received.ToArray()); await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var scanner = new ClamAvScanner(Options.Create(new ScannerSettings { Host = "127.0.0.1", Port = port }));
        Assert.Equal(expected, await scanner.IsCleanAsync(new MemoryStream(payload), timeout.Token)); await server;
    }
    [Fact] public async Task ScannerUnavailableNeverApprovesFile()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var scanner = new ClamAvScanner(Options.Create(new ScannerSettings { Host = "127.0.0.1", Port = port }));
        await Assert.ThrowsAnyAsync<SocketException>(() => scanner.IsCleanAsync(new MemoryStream([1, 2]), CancellationToken.None));
    }
}
