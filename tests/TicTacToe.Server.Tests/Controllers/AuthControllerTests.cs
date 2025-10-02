using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Controllers;
using XoHub.Server.Models;
using XoHub.Server.Services;
using Xunit;

namespace TicTacToe.Server.Tests.Controllers;

public class ThrowingInfoLogger : ILogger<AuthController>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new DummyDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Information) throw new Exception("Logger exception");
    }
}

public class DummyDisposable : IDisposable
{
    public void Dispose() { }
}

public class AuthControllerTests
{
    private readonly Mock<IKeyManager> _keyManagerMock;
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<ILogger<AuthController>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _keyManagerMock = new Mock<IKeyManager>();
        _authServiceMock = new Mock<IAuthService>();
        _loggerMock = new Mock<ILogger<AuthController>>();
        _configurationMock = new Mock<IConfiguration>();

        _controller = new AuthController(
            _keyManagerMock.Object,
            _authServiceMock.Object,
            _loggerMock.Object,
            _configurationMock.Object);

        // Setup HttpContext
        SetupHttpContext();
    }

    private void SetupHttpContext(string contentType = "application/json", long? contentLength = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = contentType;
        if (contentLength.HasValue)
        {
            httpContext.Request.ContentLength = contentLength.Value;
        }
        httpContext.Request.Headers["X-Forwarded-For"] = "192.168.1.100";
        httpContext.Request.Headers["User-Agent"] = "TestAgent/1.0";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var controllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        _controller.ControllerContext = controllerContext;
    }

    private void SetupAuthenticatedUser(string userId = "test-user", string userName = "testuser")
    {
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("username", userName)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext.HttpContext.User = principal;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var controller = new AuthController(
            _keyManagerMock.Object,
            _authServiceMock.Object,
            _loggerMock.Object,
            _configurationMock.Object);

        // Assert
        Assert.NotNull(controller);
    }

    [Fact]
    public void Constructor_WithNullKeyManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AuthController(
                null!,
                _authServiceMock.Object,
                _loggerMock.Object,
                _configurationMock.Object));

        Assert.Equal("keyManager", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AuthController(
                _keyManagerMock.Object,
                null!,
                _loggerMock.Object,
                _configurationMock.Object));

        Assert.Equal("authService", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AuthController(
                _keyManagerMock.Object,
                _authServiceMock.Object,
                null!,
                _configurationMock.Object));

        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkResult()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };
        var expectedToken = "jwt-token-123";
        var expectedUserId = "user-123";

        _authServiceMock.Setup(x => x.IsRateLimitExceededAsync(It.IsAny<string>(), "login"))
            .ReturnsAsync(false);
        _authServiceMock.Setup(x => x.RecordRequestAsync(It.IsAny<string>(), "login"))
            .Returns(Task.CompletedTask);
        _authServiceMock.Setup(x => x.AuthenticateUserAsync("testuser", "ValidPass123!"))
            .ReturnsAsync((true, expectedUserId, null));
        _keyManagerMock.Setup(x => x.GenerateJwtToken(expectedUserId, "testuser"))
            .Returns(expectedToken);

        // Act
        var result = await _controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<LoginResponse>(okResult.Value);
        Assert.Equal(expectedToken, response.Token);
        Assert.Equal(expectedUserId, response.UserId);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("Bearer", response.TokenType);
    }

    [Fact]
    public async Task Login_WithInvalidContentType_ReturnsBadRequest()
    {
        // Arrange
        SetupHttpContext("text/plain");
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Content-Type must be application/json", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithRequestTooLarge_ReturnsBadRequest()
    {
        // Arrange
        SetupHttpContext("application/json", 5000); // Over 4096 limit
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Request body must be less than 4096 bytes", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithInvalidModelState_ReturnsBadRequest()
    {
        // Arrange
        _controller.ModelState.AddModelError("UserName", "Username is required");
        var request = new LoginRequest { UserName = "", Password = "ValidPass123!" };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Please check your input data", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithInvalidUserName_ReturnsBadRequest()
    {
        // Arrange
        var request = new LoginRequest { UserName = "invalid@username", Password = "ValidPass123!" };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Username contains invalid characters", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "weak" };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Password does not meet security requirements", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithRateLimitExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        _authServiceMock.Setup(x => x.IsRateLimitExceededAsync(It.IsAny<string>(), "login"))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Rate limit exceeded", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithAuthenticationFailure_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        _authServiceMock.Setup(x => x.IsRateLimitExceededAsync(It.IsAny<string>(), "login"))
            .ReturnsAsync(false);
        _authServiceMock.Setup(x => x.RecordRequestAsync(It.IsAny<string>(), "login"))
            .Returns(Task.CompletedTask);
        _authServiceMock.Setup(x => x.AuthenticateUserAsync("testuser", "ValidPass123!"))
            .ReturnsAsync((false, null, "Invalid credentials"));

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Invalid credentials", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithException_ReturnsInternalServerError()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        _authServiceMock.Setup(x => x.IsRateLimitExceededAsync(It.IsAny<string>(), "login"))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("An unexpected error occurred", problemDetails.Detail);
    }

    [Fact]
    public async Task Login_WithNullRequest_HandlesGracefully()
    {
        // Arrange
        LoginRequest request = null!;

        // Act
        var result = await _controller.Login(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public void Logout_WithAuthenticatedUser_ReturnsOkResult()
    {
        // Arrange
        SetupAuthenticatedUser("user-123", "testuser");

        // Act
        var result = _controller.Logout();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void Logout_WithException_ReturnsInternalServerError()
    {
        // Arrange
        var controller = new AuthController(_keyManagerMock.Object, _authServiceMock.Object, new ThrowingInfoLogger(), _configurationMock.Object);
        SetupHttpContextForController(controller);
        SetupAuthenticatedUser("user-123", "testuser", controller);

        // Act
        var result = controller.Logout();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("An error occurred during logout", problemDetails.Detail);
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public void GetStatus_WithAuthenticatedUser_ReturnsOkResult()
    {
        // Arrange
        SetupAuthenticatedUser("user-123", "testuser");

        // Act
        var result = _controller.GetStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UserStatusResponse>(okResult.Value);
        Assert.Equal("user-123", response.UserId);
        Assert.Equal("testuser", response.UserName);
        Assert.True(response.IsAuthenticated);
        Assert.True(response.LastActivity <= DateTime.UtcNow);
    }

    [Fact]
    public void GetStatus_WithMissingClaims_UsesDefaultValues()
    {
        // Arrange
        var claims = new[] { new Claim("other", "value") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext.HttpContext.User = principal;

        // Act
        var result = _controller.GetStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UserStatusResponse>(okResult.Value);
        Assert.Equal("unknown", response.UserId);
        Assert.Equal("unknown", response.UserName);
        Assert.True(response.IsAuthenticated);
    }

    [Fact]
    public void GetStatus_WithException_ReturnsInternalServerError()
    {
        // Arrange
        var controller = new AuthController(_keyManagerMock.Object, _authServiceMock.Object, new ThrowingInfoLogger(), _configurationMock.Object);
        SetupHttpContextForController(controller);
        SetupAuthenticatedUser("user-123", "testuser", controller);

        // Act
        var result = controller.GetStatus();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Contains("Unable to retrieve user status", problemDetails.Detail);
    }

    #endregion

    #region Input Validation Tests

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ab", false)] // Too short
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", false)] // Too long
    [InlineData("valid_user123", true)]
    [InlineData("TestUser", true)]
    [InlineData("user_123", true)]
    [InlineData("user@name", false)] // Invalid character
    [InlineData("user name", false)] // Space not allowed
    [InlineData("user-name", false)] // Dash not allowed
    public void IsValidUserName_VariousInputs_ReturnsExpectedResult(string userName, bool expected)
    {
        // Act
        var result = InvokePrivateMethod<bool>(_controller, "IsValidUserName", userName);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("1234567", false)] // Too short
    [InlineData("password", false)] // No uppercase, digit, special char
    [InlineData("Password", false)] // No digit, special char
    [InlineData("Password1", false)] // No special char
    [InlineData("Password!", false)] // No digit
    [InlineData("Pass1!", false)] // Too short
    [InlineData("ValidPass123!", true)]
    [InlineData("Complex@Pass123", true)]
    [InlineData("Test$User999", true)]
    public void IsValidPassword_VariousInputs_ReturnsExpectedResult(string password, bool expected)
    {
        // Act
        var result = InvokePrivateMethod<bool>(_controller, "IsValidPassword", password);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Security Validation Tests

    [Fact]
    public void PerformSecurityValidationAsync_WithValidRequest_ReturnsNull()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        // Act
        var result = InvokePrivateMethod<IActionResult>(_controller, "PerformSecurityValidationAsync",
            request, "192.168.1.100", "test-request-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void PerformSecurityValidationAsync_WithInvalidContentType_ReturnsBadRequest()
    {
        // Arrange
        SetupHttpContext("text/plain");
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        // Act
        var result = InvokePrivateMethod<IActionResult>(_controller, "PerformSecurityValidationAsync",
            request, "192.168.1.100", "test-request-id");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public void PerformSecurityValidationAsync_WithLargeRequest_ReturnsBadRequest()
    {
        // Arrange
        SetupHttpContext("application/json", 5000);
        var request = new LoginRequest { UserName = "testuser", Password = "ValidPass123!" };

        // Act
        var result = InvokePrivateMethod<IActionResult>(_controller, "PerformSecurityValidationAsync",
            request, "192.168.1.100", "test-request-id");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public void PerformSecurityValidationAsync_WithInvalidUserName_ReturnsBadRequest()
    {
        // Arrange
        var request = new LoginRequest { UserName = "invalid@name", Password = "ValidPass123!" };

        // Act
        var result = InvokePrivateMethod<IActionResult>(_controller, "PerformSecurityValidationAsync",
            request, "192.168.1.100", "test-request-id");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public void PerformSecurityValidationAsync_WithInvalidPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = "weak" };

        // Act
        var result = InvokePrivateMethod<IActionResult>(_controller, "PerformSecurityValidationAsync",
            request, "192.168.1.100", "test-request-id");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    #endregion

    #region Model Tests

    [Fact]
    public void LoginResponse_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var response = new LoginResponse();

        // Assert
        Assert.Equal(string.Empty, response.Token);
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(0, response.ExpiresIn);
        Assert.Equal(string.Empty, response.TokenType);
    }

    [Fact]
    public void LoginResponse_PropertyGettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var response = new LoginResponse();
        var expectedToken = "jwt-token-123";
        var expectedUserId = "user-123";
        var expectedExpiresIn = 3600;
        var expectedTokenType = "Bearer";

        // Act
        response.Token = expectedToken;
        response.UserId = expectedUserId;
        response.ExpiresIn = expectedExpiresIn;
        response.TokenType = expectedTokenType;

        // Assert
        Assert.Equal(expectedToken, response.Token);
        Assert.Equal(expectedUserId, response.UserId);
        Assert.Equal(expectedExpiresIn, response.ExpiresIn);
        Assert.Equal(expectedTokenType, response.TokenType);
    }

    [Fact]
    public void LoginResponse_ObjectInitializer_WorksCorrectly()
    {
        // Act
        var response = new LoginResponse
        {
            Token = "jwt-token-123",
            UserId = "user-123",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        };

        // Assert
        Assert.Equal("jwt-token-123", response.Token);
        Assert.Equal("user-123", response.UserId);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("Bearer", response.TokenType);
    }

    [Fact]
    public void UserStatusResponse_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var response = new UserStatusResponse();

        // Assert
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(string.Empty, response.UserName);
        Assert.False(response.IsAuthenticated);
        Assert.Equal(default(DateTime), response.LastActivity);
    }

    [Fact]
    public void UserStatusResponse_PropertyGettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var response = new UserStatusResponse();
        var expectedUserId = "user-123";
        var expectedUserName = "testuser";
        var expectedIsAuthenticated = true;
        var expectedLastActivity = DateTime.UtcNow;

        // Act
        response.UserId = expectedUserId;
        response.UserName = expectedUserName;
        response.IsAuthenticated = expectedIsAuthenticated;
        response.LastActivity = expectedLastActivity;

        // Assert
        Assert.Equal(expectedUserId, response.UserId);
        Assert.Equal(expectedUserName, response.UserName);
        Assert.Equal(expectedIsAuthenticated, response.IsAuthenticated);
        Assert.Equal(expectedLastActivity, response.LastActivity);
    }

    [Fact]
    public void UserStatusResponse_ObjectInitializer_WorksCorrectly()
    {
        // Arrange
        var expectedLastActivity = DateTime.UtcNow;

        // Act
        var response = new UserStatusResponse
        {
            UserId = "user-123",
            UserName = "testuser",
            IsAuthenticated = true,
            LastActivity = expectedLastActivity
        };

        // Assert
        Assert.Equal("user-123", response.UserId);
        Assert.Equal("testuser", response.UserName);
        Assert.True(response.IsAuthenticated);
        Assert.Equal(expectedLastActivity, response.LastActivity);
    }

    #endregion

    private void SetupHttpContextForController(AuthController controller, string contentType = "application/json", long? contentLength = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = contentType;
        if (contentLength.HasValue)
        {
            httpContext.Request.ContentLength = contentLength.Value;
        }
        httpContext.Request.Headers["X-Forwarded-For"] = "192.168.1.100";
        httpContext.Request.Headers["User-Agent"] = "TestAgent/1.0";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var controllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        controller.ControllerContext = controllerContext;
    }

    private void SetupAuthenticatedUser(string userId = "test-user", string userName = "testuser", AuthController? controller = null)
    {
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("username", userName)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var targetController = controller ?? _controller;
        targetController.ControllerContext.HttpContext.User = principal;
    }

    private T InvokePrivateMethod<T>(object instance, string methodName, params object[] parameters)
    {
        var type = instance.GetType();
        var method = type.GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null)
        {
            throw new InvalidOperationException($"Method {methodName} not found");
        }
        var result = method.Invoke(instance, parameters);
        return result != null ? (T)result : default!;
    }
}
