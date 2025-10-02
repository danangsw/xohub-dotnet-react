using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using XoHub.Server.Controllers;
using XoHub.Server.Services;
using Xunit;

namespace TicTacToe.Server.Tests.Controllers;

public class JwksControllerTests
{
    private readonly Mock<IKeyManager> _keyManagerMock;
    private readonly Mock<ICacheWrapper> _cacheMock;
    private readonly Mock<ILogger<JwksController>> _loggerMock;
    private readonly JwksController _controller;

    public JwksControllerTests()
    {
        _keyManagerMock = new Mock<IKeyManager>();
        _cacheMock = new Mock<ICacheWrapper>();
        _loggerMock = new Mock<ILogger<JwksController>>();

        _controller = new JwksController(
            _keyManagerMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var controller = new JwksController(
            _keyManagerMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);

        // Assert
        Assert.NotNull(controller);
    }

    [Fact]
    public void Constructor_WithNullKeyManager_DoesNotThrow()
    {
        // ASP.NET Core DI handles null checks, so constructor doesn't validate
        var controller = new JwksController(
            null!,
            _cacheMock.Object,
            _loggerMock.Object);

        Assert.NotNull(controller);
    }

    [Fact]
    public void Constructor_WithNullCache_DoesNotThrow()
    {
        // ASP.NET Core DI handles null checks, so constructor doesn't validate
        var controller = new JwksController(
            _keyManagerMock.Object,
            null!,
            _loggerMock.Object);

        Assert.NotNull(controller);
    }

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // ASP.NET Core DI handles null checks, so constructor doesn't validate
        var controller = new JwksController(
            _keyManagerMock.Object,
            _cacheMock.Object,
            null!);

        Assert.NotNull(controller);
    }

    #endregion

    #region GetJwks Tests

    [Fact]
    public async Task GetJwks_CallsKeyManagerGetJwks()
    {
        // Arrange - Setup cache to return null (cache miss)
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var expectedJwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(expectedJwks);

        // Act
        await _controller.GetJwks();

        // Assert
        _keyManagerMock.Verify(x => x.GetJwks(), Times.Once);
    }

    [Fact]
    public async Task GetJwks_ReturnsContentResult()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetJwks_ReturnsApplicationJsonContentType()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
    }

    [Fact]
    public async Task GetJwks_SerializesJwksToJson()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        var key = new JsonWebKey
        {
            Kty = "RSA",
            Kid = "test-key"
        };
        jwks.Keys.Add(key);

        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.NotNull(contentResult.Content);

        // Verify it's valid JSON
        var deserialized = JsonSerializer.Deserialize<JsonWebKeySet>(contentResult.Content!);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Keys);
    }

    [Fact]
    public async Task GetJwks_WithException_Returns500StatusCode()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
    }

    [Fact]
    public async Task GetJwks_WithKeyManagerException_Returns500StatusCode()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _keyManagerMock.Setup(x => x.GetJwks())
            .Throws(new Exception("Key manager error"));

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
    }

    [Fact]
    public async Task GetJwks_WithCacheSetException_Returns500StatusCode()
    {
        // Arrange - Cache miss, then exception during cache set
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        _cacheMock.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Cache set error"));

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
    }

    [Fact]
    public async Task GetJwks_LogsInformationOnSuccess()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(new JsonWebKey { Kid = "key1" });
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        Assert.IsType<ContentResult>(result);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Generated and cached JWKS response")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetJwks_LogsErrorOnException()
    {
        // Arrange
        var expectedException = new Exception("Test exception");
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        await _controller.GetJwks();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error generating JWKS response")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetJwks_WithCacheHit_ReturnsCachedResponse()
    {
        // Arrange
        var cachedJson = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"cached-key\"}]}";
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedJson);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal(cachedJson, contentResult.Content);
        Assert.Equal("application/json", contentResult.ContentType);

        // Verify key manager was not called
        _keyManagerMock.Verify(x => x.GetJwks(), Times.Never);
    }

    [Fact]
    public async Task GetJwks_WithCacheHit_LogsDebugMessage()
    {
        // Arrange
        var cachedJson = "{\"keys\":[]}";
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedJson);

        // Act
        await _controller.GetJwks();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Returning cached JWKS response")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetJwks_WithEmptyStringCache_TreatAsNull()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        Assert.IsType<ContentResult>(result);
        _keyManagerMock.Verify(x => x.GetJwks(), Times.Once);
    }

    [Fact]
    public async Task GetJwks_VerifiesCacheKey()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        await _controller.GetJwks();

        // Assert
        _cacheMock.Verify(x => x.GetStringAsync("jwks_response", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.SetStringAsync("jwks_response", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJwks_VerifiesJsonSerializationOptions()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        var key = new JsonWebKey
        {
            Kty = "RSA",
            Kid = "test-key",
            Use = "sig"
        };
        jwks.Keys.Add(key);

        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        string? capturedJson = null;
        _cacheMock.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, value, token) => capturedJson = value);

        // Act
        await _controller.GetJwks();

        // Assert
        Assert.NotNull(capturedJson);
        Assert.DoesNotContain("  ", capturedJson); // No double spaces (compact format)
        Assert.DoesNotContain("\n", capturedJson); // No newlines
        Assert.DoesNotContain("\r", capturedJson); // No carriage returns
    }

    [Fact]
    public async Task GetJwks_WithNullHttpContext_HandlesGracefully()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Controller's HttpContext will be null in unit tests by default
        // Act
        var result = await _controller.GetJwks();

        // Assert
        Assert.IsType<ContentResult>(result);
        _cacheMock.Verify(x => x.GetStringAsync(It.IsAny<string>(), CancellationToken.None), Times.Once);
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public async Task GetJwks_UsesCompactJsonSerialization()
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var jwks = new JsonWebKeySet();
        _keyManagerMock.Setup(x => x.GetJwks())
            .Returns(jwks);

        // Act
        var result = await _controller.GetJwks();

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);

        // Compact JSON should not contain newlines or excessive whitespace
        Assert.DoesNotContain("\n", contentResult.Content);
        Assert.DoesNotContain("\r", contentResult.Content);
    }

    #endregion

    #region Attribute Tests

    [Fact]
    public void GetJwks_HasHttpGetAttribute()
    {
        // Arrange
        var methodInfo = typeof(JwksController).GetMethod("GetJwks");
        var httpGetAttribute = methodInfo?.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(httpGetAttribute);
        Assert.Equal("jwks.json", httpGetAttribute.Template);
    }

    [Fact]
    public void Controller_HasRouteAttribute()
    {
        // Arrange
        var controllerType = typeof(JwksController);
        var routeAttribute = controllerType.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(routeAttribute);
        Assert.Equal(".well-known", routeAttribute.Template);
    }

    [Fact]
    public void Controller_HasEnableRateLimitingAttribute()
    {
        // Arrange
        var controllerType = typeof(JwksController);
        var rateLimitAttribute = controllerType.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
            .Cast<EnableRateLimitingAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(rateLimitAttribute);
        Assert.Equal("JWKS", rateLimitAttribute.PolicyName);
    }

    [Fact]
    public void GetJwks_HasProducesAttribute()
    {
        // Arrange
        var methodInfo = typeof(JwksController).GetMethod("GetJwks");
        var producesAttributes = methodInfo?.GetCustomAttributes(typeof(ProducesAttribute), false)
            .Cast<ProducesAttribute>()
            .ToList();

        // Assert
        Assert.NotNull(producesAttributes);
        Assert.Single(producesAttributes);
        Assert.Equal("application/json", producesAttributes[0].ContentTypes[0]);
    }

    [Fact]
    public void GetJwks_HasProducesResponseTypeAttributes()
    {
        // Arrange
        var methodInfo = typeof(JwksController).GetMethod("GetJwks");
        var responseTypeAttributes = methodInfo?.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), false)
            .Cast<ProducesResponseTypeAttribute>()
            .ToList();

        // Assert
        Assert.NotNull(responseTypeAttributes);
        Assert.Equal(3, responseTypeAttributes.Count);

        var statusCodes = responseTypeAttributes.Select(attr => attr.StatusCode).ToList();
        Assert.Contains(200, statusCodes);
        Assert.Contains(429, statusCodes);
        Assert.Contains(500, statusCodes);
    }

    #endregion
}
