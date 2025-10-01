using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace XoHub.Server.Tests.Services;

public class KeyManagerTests : IDisposable
{
    private readonly Mock<ILogger<KeyManager>> _loggerMock = new();
    private readonly string _tempKeyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly KeyManager _manager;

    public KeyManagerTests()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "0"}, // ~1 second for testing expiration
            {"JWT:KeyRotationHours", "1"},
            {"JWT:KeyOverlapHours", "2"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        _manager = new KeyManager(_loggerMock.Object, config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempKeyPath))
            Directory.Delete(_tempKeyPath, true);
    }

    [Fact]
    public void GenerateJwtToken_InvalidArgs_Throws()
    {
        Action act1 = () => _manager.GenerateJwtToken(null!, "user");
        act1.Should().Throw<ArgumentException>();

        Action act2 = () => _manager.GenerateJwtToken("id", null!);
        act2.Should().Throw<ArgumentException>();

        Action act3 = () => _manager.GenerateJwtToken("", "user");
        act3.Should().Throw<ArgumentException>();

        Action act4 = () => _manager.GenerateJwtToken("id", "");
        act4.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GenerateJwtToken_Valid_And_ValidateAsync()
    {
        var token = _manager.GenerateJwtToken("u1", "name1");
        token.Should().NotBeNullOrEmpty();

        var (valid, principal) = await _manager.ValidateTokenAsync(token);
        valid.Should().BeTrue();
        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("u1");
        principal.FindFirst("username")?.Value.Should().Be("name1");
    }

    [Fact]
    public async Task ValidateTokenAsync_EmptyOrWhitespace_ReturnsFalse()
    {
        var (v1, p1) = await _manager.ValidateTokenAsync(null!);
        v1.Should().BeFalse(); p1.Should().BeNull();

        var (v2, p2) = await _manager.ValidateTokenAsync(" ");
        v2.Should().BeFalse(); p2.Should().BeNull();
    }

    [Fact]
    public async Task IsTokenValidAsync_ReturnsTrueForValidToken()
    {
        var token = _manager.GenerateJwtToken("u2", "n2");
        (await _manager.IsTokenValidAsync(token)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsFalse()
    {
        var (valid, principal) = await _manager.ValidateTokenAsync("invalid.token");
        valid.Should().BeFalse();
        principal.Should().BeNull();
    }

    [Fact]
    public void GetJwks_Initial_ReturnsOneKey()
    {
        var jwks = _manager.GetJwks();
        jwks.Keys.Should().HaveCount(1);
        jwks.Keys[0].Kid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RotateKeys_IncludesPreviousKey()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "0"}, // ~1 second for testing expiration
            {"JWT:KeyRotationHours", "0"}, // Force immediate rotation
            {"JWT:KeyOverlapHours", "2"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        var first = mgr.GetJwks();
        first.Keys.Should().HaveCount(1);

        mgr.RotateKeys();

        var second = mgr.GetJwks();
        second.Keys.Should().HaveCount(2);
    }

    [Fact]
    public async Task PreviousToken_IsValidWithinOverlap()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "0"}, // ~1 second for testing expiration
            {"JWT:KeyRotationHours", "0"}, // Force immediate rotation
            {"JWT:KeyOverlapHours", "2"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        var token = mgr.GenerateJwtToken("u3", "n3");

        mgr.RotateKeys();
        var (valid, _) = await mgr.ValidateTokenAsync(token);
        valid.Should().BeTrue();
    }

    [Fact]
    public async Task PreviousToken_IsInvalidAfterOverlapWindow()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "ti"},
            {"JWT:Audience", "ta"},
            {"JWT:TokenLifetimeHours", "1"},
            {"JWT:KeyRotationHours", "0"},
            {"JWT:KeyOverlapHours", "0"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        var tok = mgr.GenerateJwtToken("u4", "n4");
        mgr.RotateKeys();
        typeof(KeyManager).GetField("_lastRotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(mgr, DateTime.UtcNow.AddHours(-1));

        var (valid, _) = await mgr.ValidateTokenAsync(tok);
        valid.Should().BeFalse();
    }

    [Fact]
    public async Task Token_Expires_AfterLifetime()
    {
        var settings = new Dictionary<string, string?>
    {
        {"JWT:KeyStoragePath", _tempKeyPath},
        {"JWT:Issuer", "ti"},
        {"JWT:Audience", "ta"},
        {"JWT:TokenLifetimeHours", "0.0003"}, // ~1 second
        {"JWT:KeyRotationHours", "1"},
        {"JWT:KeyOverlapHours", "1"},
        {"JWT:ClockSkewSeconds", "0"} // <--- Add this
    };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        var tok = mgr.GenerateJwtToken("u5", "n5");
        await Task.Delay(1500); // Wait longer than token lifetime
        var (valid, _) = await mgr.ValidateTokenAsync(tok);
        valid.Should().BeFalse();
    }

    [Fact]
    public void Dispose_LogsDisposal()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "1"},
            {"JWT:KeyRotationHours", "1"},
            {"JWT:KeyOverlapHours", "2"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        // Act
        mgr.Dispose();

        // Assert
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("KeyManager disposed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_AfterRotation_LogsDisposal()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "1"},
            {"JWT:KeyRotationHours", "0"}, // Force rotation
            {"JWT:KeyOverlapHours", "2"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(_loggerMock.Object, config);

        // Rotate to have previous key
        mgr.RotateKeys();

        // Act
        mgr.Dispose();

        // Assert
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("KeyManager disposed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}