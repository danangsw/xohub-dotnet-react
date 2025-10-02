using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace TicTacToe.Server.Tests.Services;

public class CacheWrapperTests
{
    private readonly Mock<IDistributedCache> _distributedCacheMock;
    private readonly CacheWrapper _cacheWrapper;

    public CacheWrapperTests()
    {
        _distributedCacheMock = new Mock<IDistributedCache>();
        _cacheWrapper = new CacheWrapper(_distributedCacheMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDistributedCache_CreatesInstance()
    {
        // Arrange & Act
        var wrapper = new CacheWrapper(_distributedCacheMock.Object);

        // Assert
        Assert.NotNull(wrapper);
    }

    [Fact]
    public void Constructor_WithNullDistributedCache_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CacheWrapper(null!));
    }

    #endregion

    #region GetStringAsync Tests

    [Fact]
    public async Task GetStringAsync_WithValidKey_CallsDistributedCache()
    {
        // Arrange
        var key = "test-key";
        var token = CancellationToken.None;
        var expectedValue = "test-value";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedValue);

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync(expectedBytes);

        // Act
        var result = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Equal(expectedValue, result);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithNullValue_ReturnsNull()
    {
        // Arrange
        var key = "test-key";
        var token = CancellationToken.None;

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Null(result);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithEmptyKey_CallsDistributedCache()
    {
        // Arrange
        var key = string.Empty;
        var token = CancellationToken.None;

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Null(result);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithCancellationToken_PassesTokenCorrectly()
    {
        // Arrange
        var key = "test-key";
        var token = new CancellationToken(true); // Cancelled token

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _cacheWrapper.GetStringAsync(key, token));

        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithException_PropagatesException()
    {
        // Arrange
        var key = "test-key";
        var token = CancellationToken.None;
        var expectedException = new InvalidOperationException("Cache error");

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cacheWrapper.GetStringAsync(key, token));

        Assert.Equal(expectedException.Message, actualException.Message);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithLongKey_CallsDistributedCache()
    {
        // Arrange
        var key = new string('x', 1000); // Very long key
        var token = CancellationToken.None;
        var expectedValue = "value-for-long-key";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedValue);

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync(expectedBytes);

        // Act
        var result = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Equal(expectedValue, result);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WithSpecialCharactersInKey_CallsDistributedCache()
    {
        // Arrange
        var key = "test:key-with_special.chars@domain.com";
        var token = CancellationToken.None;
        var expectedValue = "special-value";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedValue);

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync(expectedBytes);

        // Act
        var result = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Equal(expectedValue, result);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    #endregion

    #region SetStringAsync Tests

    [Fact]
    public async Task SetStringAsync_WithValidKeyAndValue_CallsDistributedCache()
    {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithEmptyValue_CallsDistributedCache()
    {
        // Arrange
        var key = "test-key";
        var value = string.Empty;
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithEmptyKey_CallsDistributedCache()
    {
        // Arrange
        var key = string.Empty;
        var value = "test-value";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithCancellationToken_PassesTokenCorrectly()
    {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        var token = new CancellationToken(true); // Cancelled token
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _cacheWrapper.SetStringAsync(key, value, token));

        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithException_PropagatesException()
    {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);
        var expectedException = new InvalidOperationException("Cache write error");

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cacheWrapper.SetStringAsync(key, value, token));

        Assert.Equal(expectedException.Message, actualException.Message);
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithLargeValue_CallsDistributedCache()
    {
        // Arrange
        var key = "test-key";
        var value = new string('x', 10000); // Large value
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithJsonValue_CallsDistributedCache()
    {
        // Arrange
        var key = "jwks_response";
        var value = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"test-key\",\"use\":\"sig\"}]}";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithSpecialCharactersInValue_CallsDistributedCache()
    {
        // Arrange
        var key = "test-key";
        var value = "Value with special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WithUnicodeValue_CallsDistributedCache()
    {
        // Arrange
        var key = "unicode-key";
        var value = "Unicode value: 你好世界 🌍 émojis";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
    }

    #endregion

    #region Integration-Style Tests

    [Fact]
    public async Task CacheWrapper_GetAndSetOperations_WorkTogether()
    {
        // Arrange
        var key = "integration-key";
        var value = "integration-value";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);
        byte[]? storedBytes = null;

        // Setup to capture set value and return it on get
        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((k, v, o, t) => storedBytes = v)
            .Returns(Task.CompletedTask);

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ReturnsAsync(() => storedBytes);

        // Act
        await _cacheWrapper.SetStringAsync(key, value, token);
        var retrievedValue = await _cacheWrapper.GetStringAsync(key, token);

        // Assert
        Assert.Equal(value, retrievedValue);
        _distributedCacheMock.Verify(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
        _distributedCacheMock.Verify(x => x.GetAsync(key, token), Times.Once);
    }

    [Fact]
    public async Task CacheWrapper_MultipleOperations_CallsDistributedCacheCorrectly()
    {
        // Arrange
        var key1 = "key1";
        var key2 = "key2";
        var value1 = "value1";
        var value2 = "value2";
        var token = CancellationToken.None;
        var bytes1 = Encoding.UTF8.GetBytes(value1);
        var bytes2 = Encoding.UTF8.GetBytes(value2);

        _distributedCacheMock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), token))
            .Returns(Task.CompletedTask);
        _distributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), token))
            .ReturnsAsync((byte[]?)null);

        // Act
        await _cacheWrapper.SetStringAsync(key1, value1, token);
        await _cacheWrapper.SetStringAsync(key2, value2, token);
        await _cacheWrapper.GetStringAsync(key1, token);
        await _cacheWrapper.GetStringAsync(key2, token);

        // Assert
        _distributedCacheMock.Verify(x => x.SetAsync(key1, bytes1, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
        _distributedCacheMock.Verify(x => x.SetAsync(key2, bytes2, It.IsAny<DistributedCacheEntryOptions>(), token), Times.Once);
        _distributedCacheMock.Verify(x => x.GetAsync(key1, token), Times.Once);
        _distributedCacheMock.Verify(x => x.GetAsync(key2, token), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GetStringAsync_WithTimeoutException_PropagatesException()
    {
        // Arrange
        var key = "timeout-key";
        var token = CancellationToken.None;
        var expectedException = new TimeoutException("Cache operation timed out");

        _distributedCacheMock.Setup(x => x.GetAsync(key, token))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<TimeoutException>(() =>
            _cacheWrapper.GetStringAsync(key, token));

        Assert.Equal(expectedException.Message, actualException.Message);
    }

    [Fact]
    public async Task SetStringAsync_WithTimeoutException_PropagatesException()
    {
        // Arrange
        var key = "timeout-key";
        var value = "timeout-value";
        var token = CancellationToken.None;
        var expectedBytes = Encoding.UTF8.GetBytes(value);
        var expectedException = new TimeoutException("Cache write operation timed out");

        _distributedCacheMock.Setup(x => x.SetAsync(key, expectedBytes, It.IsAny<DistributedCacheEntryOptions>(), token))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<TimeoutException>(() =>
            _cacheWrapper.SetStringAsync(key, value, token));

        Assert.Equal(expectedException.Message, actualException.Message);
    }

    #endregion
}