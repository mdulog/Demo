using Microsoft.Extensions.Configuration;
using Pacevite.Api.Infrastructure.Auth;
using TUnit.Assertions;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class JwtTokenServiceTests
{
    private IJwtTokenService _sut = null!;

    [Before(Test)]
    public void SetUp()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-for-testing-only-32c",
                ["Jwt:Issuer"] = "pacevite-test",
                ["Jwt:Audience"] = "pacevite-test",
                ["Jwt:AccessTokenExpiryMinutes"] = "15"
            })
            .Build();
        _sut = new JwtTokenService(config);
    }

    [Test]
    public async Task generate_refresh_token_returns_64_byte_base64_string()
    {
        var token = _sut.GenerateRefreshToken();
        var bytes = Convert.FromBase64String(token);
        await Assert.That(bytes.Length).IsEqualTo(64);
    }

    [Test]
    public async Task generate_refresh_token_returns_unique_values()
    {
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();
        await Assert.That(token1).IsNotEqualTo(token2);
    }

    [Test]
    public async Task hash_token_is_deterministic()
    {
        var raw = _sut.GenerateRefreshToken();
        var hash1 = _sut.HashToken(raw);
        var hash2 = _sut.HashToken(raw);
        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task hash_token_produces_different_hashes_for_different_inputs()
    {
        var hash1 = _sut.HashToken("token-a");
        var hash2 = _sut.HashToken("token-b");
        await Assert.That(hash1).IsNotEqualTo(hash2);
    }
}
