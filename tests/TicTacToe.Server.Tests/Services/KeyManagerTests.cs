using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace XoHub.Server.Tests.Services;

public class ThrowingLogger : ILogger<KeyManager>
{
    public IDisposable BeginScope<TState>(TState state) => new DummyDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) throw new Exception("Logger exception");
    }
}

public class DummyDisposable : IDisposable
{
    public void Dispose() { }
}

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

    [Fact]
    public async Task ValidateTokenAsync_LoggerThrowsException_ReturnsFalse()
    {
        var settings = new Dictionary<string, string?>
        {
            {"JWT:KeyStoragePath", _tempKeyPath},
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:TokenLifetimeHours", "1"},
            {"JWT:KeyRotationHours", "1"},
            {"JWT:KeyOverlapHours", "0"} // No overlap to force warning log
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var mgr = new KeyManager(new ThrowingLogger(), config);

        // Act - use invalid token to trigger the warning log which throws
        var (valid, principal) = await mgr.ValidateTokenAsync("invalid.token");

        // Assert
        valid.Should().BeFalse();
        principal.Should().BeNull();
    }

    [Fact]
    public void LoadExistingKeys_LoadsPreviousKey_WhenExists()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Generate test keys
            using var currentRsa = RSA.Create(2048);
            using var previousRsa = RSA.Create(2048);
            var currentKeyId = "test-current-key";
            var previousKeyId = "test-previous-key";

            // Save current key
            var currentPem = currentRsa.ExportRSAPrivateKeyPem();
            File.WriteAllText(Path.Combine(tempPath, "current.pem"), currentPem);

            // Save previous key
            var previousPem = previousRsa.ExportRSAPrivateKeyPem();
            File.WriteAllText(Path.Combine(tempPath, "previous.pem"), previousPem);

            // Save metadata
            var metadata = new KeyMetadata
            {
                CurrentKeyId = currentKeyId,
                PreviousKeyId = previousKeyId,
                LastRotation = DateTime.UtcNow.AddHours(-1)
            };
            var metadataJson = JsonSerializer.Serialize(metadata);
            File.WriteAllText(Path.Combine(tempPath, "metadata.json"), metadataJson);

            // Create KeyManager - should load existing keys
            var settings = new Dictionary<string, string?>
            {
                {"JWT:KeyStoragePath", tempPath},
                {"JWT:Issuer", "TestIssuer"},
                {"JWT:Audience", "TestAudience"},
                {"JWT:TokenLifetimeHours", "1"},
                {"JWT:KeyRotationHours", "1"},
                {"JWT:KeyOverlapHours", "2"}
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var mgr = new KeyManager(_loggerMock.Object, config);

            // Verify previous key was loaded (JWKS should have 2 keys since overlap > 0)
            var jwks = mgr.GetJwks();
            jwks.Keys.Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void LoadExistingKeys_InvalidMetadata_LogsError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Generate test key
            using var currentRsa = RSA.Create(2048);

            // Save current key
            var currentPem = currentRsa.ExportRSAPrivateKeyPem();
            File.WriteAllText(Path.Combine(tempPath, "current.pem"), currentPem);

            // Save invalid metadata
            File.WriteAllText(Path.Combine(tempPath, "metadata.json"), "invalid json");

            // Create KeyManager - should catch exception and generate new keys
            var settings = new Dictionary<string, string?>
            {
                {"JWT:KeyStoragePath", tempPath},
                {"JWT:Issuer", "TestIssuer"},
                {"JWT:Audience", "TestAudience"},
                {"JWT:TokenLifetimeHours", "1"},
                {"JWT:KeyRotationHours", "1"},
                {"JWT:KeyOverlapHours", "2"}
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var mgr = new KeyManager(_loggerMock.Object, config);

            // Verify error was logged
            _loggerMock.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to load existing keys")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }
}