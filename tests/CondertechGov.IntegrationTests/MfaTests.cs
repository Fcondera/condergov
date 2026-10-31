using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace CondertechGov.IntegrationTests;
[Collection("api")]
public sealed class MfaTests(ApiFixture f)
{
    [Fact] public async Task TotpEnrollmentRevokesPreviousSessionsAndRequiresAuthenticator()
    {
        await f.Provision("mfa-city");
        using var user = await f.Login("mfa-city");
        var setup = await user.PostAsJsonAsync("/api/v1/auth/mfa/setup", new { password = ApiFixture.Password });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var setupJson = await setup.Content.ReadFromJsonAsync<JsonElement>();
        var key = setupJson.GetProperty("sharedKey").GetString()!;
        user.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", setupJson.GetProperty("tokens").GetProperty("accessToken").GetString());
        var code = Totp(key);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync("/api/v1/auth/mfa/enable", new { code })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/auth/me")).StatusCode);
        using var client = f.Factory.CreateClient();
        var noOtp = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = "mfa-city", email = "admin@example.gov.br", password = ApiFixture.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, noOtp.StatusCode);
        var withOtp = await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = "mfa-city", email = "admin@example.gov.br", password = ApiFixture.Password, totp = Totp(key) });
        Assert.Equal(HttpStatusCode.OK, withOtp.StatusCode);
    }
    private static string Totp(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = []; int accumulator = 0, bits = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            accumulator = (accumulator << 5) | alphabet.IndexOf(c); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(accumulator >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter); var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % 1_000_000).ToString("D6");
    }
}
