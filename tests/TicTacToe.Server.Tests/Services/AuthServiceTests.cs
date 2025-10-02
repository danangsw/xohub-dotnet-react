using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace TicTacToe.Server.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly Mock<ICacheWrapper> _cacheMock;
    private readonly Mock<IConfigurationService> _configurationServiceMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _loggerMock = new Mock<ILogger<AuthService>>();
        _cacheMock = new Mock<ICacheWrapper>();
        _configurationServiceMock = new Mock<IConfigurationService>();

        // Setup default configuration values
        SetupConfigurationDefaults();

        _authService = new AuthService(
            _loggerMock.Object,
            _cacheMock.Object,
            _configurationServiceMock.Object);
    }

    private void SetupConfigurationDefaults()
    {
        // Setup configuration service with default values
        _configurationServiceMock.Setup(x => x.GetMaxFailedLoginAttempts()).Returns(5);
        _configurationServiceMock.Setup(x => x.GetAccountLockoutMinutes()).Returns(15);
        _configurationServiceMock.Setup(x => x.GetRateLimitMaxRequests()).Returns(10);
        _configurationServiceMock.Setup(x => x.GetRateLimitWindowMinutes()).Returns(15);
        _configurationServiceMock.Setup(x => x.GetFailedLoginTrackingHours()).Returns(24);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange, Act & Assert
        var service = new AuthService(
            _loggerMock.Object,
            _cacheMock.Object,
            _configurationServiceMock.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            null!,
            _cacheMock.Object,
            _configurationServiceMock.Object));
    }

    [Fact]
    public void Constructor_WithNullCache_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _loggerMock.Object,
            null!,
            _configurationServiceMock.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _loggerMock.Object,
            _cacheMock.Object,
            null!));
    }

    [Fact]
    public void Constructor_WithCustomConfiguration_UsesConfiguredValues()
    {
        // Arrange
        var customConfigService = new Mock<IConfigurationService>();
        customConfigService.Setup(x => x.GetMaxFailedLoginAttempts()).Returns(3);
        customConfigService.Setup(x => x.GetAccountLockoutMinutes()).Returns(30);
        customConfigService.Setup(x => x.GetRateLimitMaxRequests()).Returns(5);
        customConfigService.Setup(x => x.GetRateLimitWindowMinutes()).Returns(10);
        customConfigService.Setup(x => x.GetFailedLoginTrackingHours()).Returns(12);

        // Act
        var service = new AuthService(
            _loggerMock.Object,
            _cacheMock.Object,
            customConfigService.Object);

        // Assert
        Assert.NotNull(service);
        customConfigService.Verify(x => x.GetMaxFailedLoginAttempts(), Times.Once);
        customConfigService.Verify(x => x.GetAccountLockoutMinutes(), Times.Once);
        customConfigService.Verify(x => x.GetRateLimitMaxRequests(), Times.Once);
        customConfigService.Verify(x => x.GetRateLimitWindowMinutes(), Times.Once);
        customConfigService.Verify(x => x.GetFailedLoginTrackingHours(), Times.Once);
    }

    #endregion

    #region AuthenticateUserAsync Tests

    [Fact]
    public async Task AuthenticateUserAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // No lockout

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("user_admin_001", result.UserId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateUserAsync_WithInvalidCredentials_ReturnsFailure()
    {
        // Arrange
        var userName = "admin";
        var password = "WrongPassword";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // No lockout

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Equal("Invalid credentials", result.ErrorMessage);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("username", null)]
    [InlineData("username", "")]
    [InlineData("username", "   ")]
    public async Task AuthenticateUserAsync_WithInvalidInput_ReturnsFailure(string? userName, string? password)
    {
        // Act
        var result = await _authService.AuthenticateUserAsync(userName!, password!);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Equal("Invalid credentials", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateUserAsync_WithLockedAccount_ReturnsFailure()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";
        var lockoutInfo = new
        {
            UserName = userName,
            FailedAttempts = 5,
            LockoutUntil = DateTime.UtcNow.AddMinutes(10),
            LockedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(lockoutInfo));

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Equal("Account temporarily locked due to multiple failed attempts", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateUserAsync_WithExpiredLockout_AllowsLogin()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";
        var expiredLockoutInfo = new
        {
            UserName = userName,
            FailedAttempts = 5,
            LockoutUntil = DateTime.UtcNow.AddMinutes(-5), // Expired
            LockedAt = DateTime.UtcNow.AddMinutes(-20)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expiredLockoutInfo));

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("user_admin_001", result.UserId);
        Assert.Null(result.ErrorMessage);

        // Verify cache removal was called (twice: once for expired lockout check, once for reset)
        _cacheMock.Verify(x => x.RemoveAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AuthenticateUserAsync_WithException_ReturnsServiceError()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Cache error"));

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Equal("Authentication service temporarily unavailable", result.ErrorMessage);
    }

    [Theory]
    [InlineData("admin", "AdminPass123!", "user_admin_001")]
    [InlineData("player1", "Player123!", "user_player1_002")]
    [InlineData("player2", "Player456!", "user_player2_003")]
    [InlineData("testuser", "TestPass123!", "user_test_004")]
    public async Task AuthenticateUserAsync_WithAllValidUsers_ReturnsCorrectUserId(string userName, string password, string expectedUserId)
    {
        // Arrange
        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedUserId, result.UserId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateUserAsync_WithCaseInsensitiveUserName_Works()
    {
        // Arrange
        var userName = "ADMIN"; // Uppercase
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("user_admin_001", result.UserId);
    }

    [Fact]
    public async Task AuthenticateUserAsync_LogsWarningOnInvalidCredentials()
    {
        // Arrange
        var userName = "admin";
        var password = "WrongPassword";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed authentication attempt")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateUserAsync_LogsInformationOnSuccess()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Successful authentication")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateUserAsync_LogsErrorOnException()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";
        var expectedException = new Exception("Test exception");

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Unexpected error during authentication")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region IsAccountLockedAsync Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsAccountLockedAsync_WithInvalidUserName_ReturnsFalse(string? userName)
    {
        // Act
        var result = await _authService.IsAccountLockedAsync(userName!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsAccountLockedAsync_WithNoLockoutData_ReturnsFalse()
    {
        // Arrange
        var userName = "testuser";

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.IsAccountLockedAsync(userName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsAccountLockedAsync_WithActiveLockout_ReturnsTrue()
    {
        // Arrange
        var userName = "testuser";
        var lockoutInfo = new
        {
            UserName = userName,
            FailedAttempts = 5,
            LockoutUntil = DateTime.UtcNow.AddMinutes(10),
            LockedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(lockoutInfo));

        // Act
        var result = await _authService.IsAccountLockedAsync(userName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsAccountLockedAsync_WithExpiredLockout_ReturnsFalseAndRemovesCache()
    {
        // Arrange
        var userName = "testuser";
        var expiredLockoutInfo = new
        {
            UserName = userName,
            FailedAttempts = 5,
            LockoutUntil = DateTime.UtcNow.AddMinutes(-1), // Expired
            LockedAt = DateTime.UtcNow.AddMinutes(-16)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expiredLockoutInfo));

        // Act
        var result = await _authService.IsAccountLockedAsync(userName);

        // Assert
        Assert.False(result);
        _cacheMock.Verify(x => x.RemoveAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsAccountLockedAsync_WithInvalidJson_ReturnsFalseAndRemovesCache()
    {
        // Arrange
        var userName = "testuser";

        _cacheMock.Setup(x => x.GetStringAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        // Act
        var result = await _authService.IsAccountLockedAsync(userName);

        // Assert
        Assert.False(result);
        _cacheMock.Verify(x => x.RemoveAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RecordFailedLoginAttemptAsync Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordFailedLoginAttemptAsync_WithInvalidUserName_DoesNothing(string? userName)
    {
        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName!);

        // Assert
        _cacheMock.Verify(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordFailedLoginAttemptAsync_FirstAttempt_RecordsFailure()
    {
        // Arrange
        var userName = "testuser";

        _cacheMock.Setup(x => x.GetStringAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"failed_attempts:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"Count\":1")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailedLoginAttemptAsync_MultipleAttempts_IncrementsCount()
    {
        // Arrange
        var userName = "testuser";
        var existingAttempts = new
        {
            Count = 2,
            ResetTime = DateTime.UtcNow.AddHours(1)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(existingAttempts));

        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"failed_attempts:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"Count\":3")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailedLoginAttemptAsync_ReachesMaxAttempts_LocksAccount()
    {
        // Arrange
        var userName = "testuser";
        var existingAttempts = new
        {
            Count = 4, // One less than max (5)
            ResetTime = DateTime.UtcNow.AddHours(1)
        };

        _cacheMock.Setup(x => x.GetStringAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(existingAttempts));

        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName);

        // Assert
        // Verify account lockout was set
        _cacheMock.Verify(x => x.SetStringAsync(
            $"account_lockout:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"FailedAttempts\":5")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Account locked")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordFailedLoginAttemptAsync_WithExpiredAttempts_ResetsCount()
    {
        // Arrange
        var userName = "testuser";
        var expiredAttempts = new
        {
            Count = 3,
            ResetTime = DateTime.UtcNow.AddMinutes(-1) // Expired
        };

        _cacheMock.Setup(x => x.GetStringAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expiredAttempts));

        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"failed_attempts:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"Count\":1")), // Reset to 1
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailedLoginAttemptAsync_WithInvalidJson_ResetsCount()
    {
        // Arrange
        var userName = "testuser";

        _cacheMock.Setup(x => x.GetStringAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        // Act
        await _authService.RecordFailedLoginAttemptAsync(userName);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"failed_attempts:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"Count\":1")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ResetFailedLoginAttemptsAsync Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResetFailedLoginAttemptsAsync_WithInvalidUserName_DoesNothing(string? userName)
    {
        // Act
        await _authService.ResetFailedLoginAttemptsAsync(userName!);

        // Assert
        _cacheMock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetFailedLoginAttemptsAsync_WithValidUserName_RemovesBothCacheEntries()
    {
        // Arrange
        var userName = "testuser";

        // Act
        await _authService.ResetFailedLoginAttemptsAsync(userName);

        // Assert
        _cacheMock.Verify(x => x.RemoveAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region IsRateLimitExceededAsync Tests

    [Theory]
    [InlineData(null, "action")]
    [InlineData("", "action")]
    [InlineData("   ", "action")]
    [InlineData("identifier", null)]
    [InlineData("identifier", "")]
    [InlineData("identifier", "   ")]
    public async Task IsRateLimitExceededAsync_WithInvalidInput_ReturnsFalse(string? identifier, string? action)
    {
        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier!, action!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsRateLimitExceededAsync_WithNoExistingData_ReturnsFalse()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsRateLimitExceededAsync_WithValidData_BelowLimit_ReturnsFalse()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";
        var rateLimitInfo = new
        {
            Count = 5, // Below limit (10)
            ResetTime = DateTime.UtcNow.AddMinutes(10),
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(rateLimitInfo));

        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsRateLimitExceededAsync_WithValidData_AtLimit_ReturnsTrue()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";
        var rateLimitInfo = new
        {
            Count = 10, // At limit
            ResetTime = DateTime.UtcNow.AddMinutes(10),
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(rateLimitInfo));

        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsRateLimitExceededAsync_WithExpiredData_ReturnsFalse()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";
        var expiredRateLimitInfo = new
        {
            Count = 15, // Above limit but expired
            ResetTime = DateTime.UtcNow.AddMinutes(-1), // Expired
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expiredRateLimitInfo));

        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsRateLimitExceededAsync_WithInvalidJson_ReturnsFalse()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        // Act
        var result = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RecordRequestAsync Tests

    [Theory]
    [InlineData(null, "action")]
    [InlineData("", "action")]
    [InlineData("   ", "action")]
    [InlineData("identifier", null)]
    [InlineData("identifier", "")]
    [InlineData("identifier", "   ")]
    public async Task RecordRequestAsync_WithInvalidInput_DoesNothing(string? identifier, string? action)
    {
        // Act
        await _authService.RecordRequestAsync(identifier!, action!);

        // Assert
        _cacheMock.Verify(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordRequestAsync_FirstRequest_RecordsOne()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _authService.RecordRequestAsync(identifier, action);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"rate_limit:{action}:{identifier}",
            It.Is<string>(s => s.Contains("\"Count\":1")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRequestAsync_MultipleRequests_IncrementsCount()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";
        var existingRateLimit = new
        {
            Count = 3,
            ResetTime = DateTime.UtcNow.AddMinutes(10),
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(existingRateLimit));

        // Act
        await _authService.RecordRequestAsync(identifier, action);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"rate_limit:{action}:{identifier}",
            It.Is<string>(s => s.Contains("\"Count\":4")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRequestAsync_WithExpiredData_ResetsCount()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";
        var expiredRateLimit = new
        {
            Count = 8,
            ResetTime = DateTime.UtcNow.AddMinutes(-1), // Expired
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expiredRateLimit));

        // Act
        await _authService.RecordRequestAsync(identifier, action);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"rate_limit:{action}:{identifier}",
            It.Is<string>(s => s.Contains("\"Count\":1")), // Reset to 1
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRequestAsync_WithInvalidJson_ResetsCount()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        // Act
        await _authService.RecordRequestAsync(identifier, action);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"rate_limit:{action}:{identifier}",
            It.Is<string>(s => s.Contains("\"Count\":1")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRequestAsync_SetsCorrectCacheExpiration()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _authService.RecordRequestAsync(identifier, action);

        // Assert
        _cacheMock.Verify(x => x.SetStringAsync(
            $"rate_limit:{action}:{identifier}",
            It.IsAny<string>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(15)), // Default rate limit window
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task AuthenticationWorkflow_SuccessfulLogin_ClearsFailedAttempts()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.True(result.Success);
        _cacheMock.Verify(x => x.RemoveAsync($"failed_attempts:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync($"account_lockout:{userName.ToLowerInvariant()}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticationWorkflow_FailedLogin_RecordsAttempt()
    {
        // Arrange
        var userName = "admin";
        var password = "WrongPassword";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.False(result.Success);
        _cacheMock.Verify(x => x.SetStringAsync(
            $"failed_attempts:{userName.ToLowerInvariant()}",
            It.Is<string>(s => s.Contains("\"Count\":1")),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RateLimitingWorkflow_MultipleRequests_ExceedsLimit()
    {
        // Arrange
        var identifier = "192.168.1.1";
        var action = "login";

        // First request - no existing data
        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act - Record first request
        await _authService.RecordRequestAsync(identifier, action);
        var isFirstRequestExceeded = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Setup for subsequent requests - simulate 10 requests
        var rateLimitInfo = new
        {
            Count = 10, // At limit
            ResetTime = DateTime.UtcNow.AddMinutes(10),
            Action = action,
            Identifier = identifier
        };

        _cacheMock.Setup(x => x.GetStringAsync($"rate_limit:{action}:{identifier}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(rateLimitInfo));

        var isLimitExceeded = await _authService.IsRateLimitExceededAsync(identifier, action);

        // Assert
        Assert.False(isFirstRequestExceeded);
        Assert.True(isLimitExceeded);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task AuthService_HandlesLargeUserNames_Gracefully()
    {
        // Arrange
        var longUserName = new string('a', 1000);
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(longUserName, password);

        // Assert
        Assert.False(result.Success); // Should fail because user doesn't exist
        Assert.Equal("Invalid credentials", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthService_HandlesSpecialCharactersInUserName()
    {
        // Arrange
        var specialUserName = "user@domain.com";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.AuthenticateUserAsync(specialUserName, password);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid credentials", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthService_HandlesCacheFailures_Gracefully()
    {
        // Arrange
        var userName = "admin";
        var password = "AdminPass123!";

        _cacheMock.Setup(x => x.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        // Act
        var result = await _authService.AuthenticateUserAsync(userName, password);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication service temporarily unavailable", result.ErrorMessage);
    }

    #endregion
}
