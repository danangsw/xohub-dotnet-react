using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Controllers;
using Xunit;

namespace TicTacToe.Server.Tests.Controllers;

/// <summary>
/// Test controller that inherits from ApiControllerBase for testing purposes
/// </summary>
public class TestApiController : ApiControllerBase
{
    public TestApiController(ILogger<TestApiController> logger, IConfiguration configuration)
        : base(logger, configuration)
    {
    }

    // Public wrapper methods for testing protected methods
    public string PublicGetRequestId() => GetRequestId();
    public string PublicGetClientIP() => GetClientIP();
    public string PublicGetUserAgent() => GetUserAgent();
    public IActionResult PublicCreateProblemDetails(string title, string detail, int statusCode, string requestId, List<string>? errors = null)
        => CreateProblemDetails(title, detail, statusCode, requestId, errors);
    public void PublicAddSecurityHeaders(string requestId) => AddSecurityHeaders(requestId);
    public IActionResult PublicApiSuccess<T>(T data, string? message = null) => ApiSuccess(data, message);
    public IActionResult PublicApiError(string title, string detail, int statusCode = StatusCodes.Status400BadRequest)
        => ApiError(title, detail, statusCode);
}

public class BaseControllerTests
{
    private readonly Mock<ILogger<TestApiController>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly TestApiController _controller;
    private readonly DefaultHttpContext _httpContext;

    public BaseControllerTests()
    {
        _loggerMock = new Mock<ILogger<TestApiController>>();
        _configurationMock = new Mock<IConfiguration>();
        _controller = new TestApiController(_loggerMock.Object, _configurationMock.Object);

        // Setup HttpContext
        _httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = _httpContext
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var controller = new TestApiController(_loggerMock.Object, _configurationMock.Object);

        // Assert
        Assert.NotNull(controller);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestApiController(null!, _configurationMock.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestApiController(_loggerMock.Object, null!));
    }

    #endregion

    #region GetRequestId Tests

    [Fact]
    public void GetRequestId_ReturnsValidGuidString()
    {
        // Arrange & Act
        var requestId = _controller.PublicGetRequestId();

        // Assert
        Assert.NotNull(requestId);
        Assert.True(Guid.TryParse(requestId, out _));
    }

    [Fact]
    public void GetRequestId_ReturnsUniqueValues()
    {
        // Arrange & Act
        var requestId1 = _controller.PublicGetRequestId();
        var requestId2 = _controller.PublicGetRequestId();

        // Assert
        Assert.NotEqual(requestId1, requestId2);
    }

    #endregion

    #region GetClientIP Tests

    [Fact]
    public void GetClientIP_WithRemoteIpAddress_ReturnsRemoteIpAddress()
    {
        // Arrange
        var expectedIp = "192.168.1.100";
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse(expectedIp);

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal(expectedIp, result);
    }

    [Fact]
    public void GetClientIP_WithXForwardedForHeader_ReturnsFirstForwardedIp()
    {
        // Arrange
        var forwardedIps = "203.0.113.1, 198.51.100.1, 192.0.2.1";
        _httpContext.Request.Headers["X-Forwarded-For"] = forwardedIps;

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("203.0.113.1", result);
    }

    [Fact]
    public void GetClientIP_WithXForwardedForHeader_IgnoresRemoteIpAddress()
    {
        // Arrange
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        _httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.1";

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("203.0.113.1", result);
    }

    [Fact]
    public void GetClientIP_WithXRealIpHeader_ReturnsRealIp()
    {
        // Arrange
        var realIp = "198.51.100.1";
        _httpContext.Request.Headers["X-Real-IP"] = realIp;

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal(realIp, result);
    }

    [Fact]
    public void GetClientIP_WithXRealIpHeader_TakesPrecedenceOverXForwardedFor()
    {
        // Arrange
        _httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.1";
        _httpContext.Request.Headers["X-Real-IP"] = "198.51.100.1";

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("198.51.100.1", result);
    }

    [Fact]
    public void GetClientIP_WithNoIpHeaders_ReturnsUnknown()
    {
        // Arrange - HttpContext is already set up with no headers

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetClientIP_WithNullRemoteIpAddress_ReturnsUnknown()
    {
        // Arrange
        _httpContext.Connection.RemoteIpAddress = null;

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetClientIP_WithEmptyXForwardedForHeader_FallsBackToRemoteIp()
    {
        // Arrange
        var expectedIp = "192.168.1.100";
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse(expectedIp);
        _httpContext.Request.Headers["X-Forwarded-For"] = "";

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal(expectedIp, result);
    }

    #endregion

    #region GetUserAgent Tests

    [Fact]
    public void GetUserAgent_WithUserAgentHeader_ReturnsUserAgent()
    {
        // Arrange
        var expectedUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        _httpContext.Request.Headers["User-Agent"] = expectedUserAgent;

        // Act
        var result = _controller.PublicGetUserAgent();

        // Assert
        Assert.Equal(expectedUserAgent, result);
    }

    [Fact]
    public void GetUserAgent_WithoutUserAgentHeader_ReturnsUnknown()
    {
        // Arrange - No User-Agent header set

        // Act
        var result = _controller.PublicGetUserAgent();

        // Assert
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetUserAgent_WithEmptyUserAgentHeader_ReturnsEmptyString()
    {
        // Arrange
        _httpContext.Request.Headers["User-Agent"] = "";

        // Act
        var result = _controller.PublicGetUserAgent();

        // Assert
        Assert.Equal("", result);
    }

    #endregion

    #region CreateProblemDetails Tests

    [Fact]
    public void CreateProblemDetails_WithBasicParameters_ReturnsProblemDetailsResponse()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";
        var statusCode = StatusCodes.Status400BadRequest;
        var requestId = "test-request-id";
        _httpContext.Request.Path = "/api/test";

        // Act
        var result = _controller.PublicCreateProblemDetails(title, detail, statusCode, requestId) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(statusCode, result.StatusCode);

        var problemDetails = result.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.Equal(title, problemDetails.Title);
        Assert.Equal(detail, problemDetails.Detail);
        Assert.Equal(statusCode, problemDetails.Status);
        Assert.Equal("/api/test", problemDetails.Instance);
        Assert.Equal(requestId, problemDetails.Extensions["requestId"]);
    }

    [Fact]
    public void CreateProblemDetails_WithErrors_AddsErrorsToExtensions()
    {
        // Arrange
        var title = "Validation Error";
        var detail = "Multiple validation errors occurred";
        var statusCode = StatusCodes.Status400BadRequest;
        var requestId = "test-request-id";
        var errors = new List<string> { "Field1 is required", "Field2 is invalid" };

        // Act
        var result = _controller.PublicCreateProblemDetails(title, detail, statusCode, requestId, errors) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        var problemDetails = result.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.Equal(errors, problemDetails.Extensions["errors"]);
    }

    [Fact]
    public void CreateProblemDetails_WithNullErrors_DoesNotAddErrorsExtension()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";
        var statusCode = StatusCodes.Status400BadRequest;
        var requestId = "test-request-id";

        // Act
        var result = _controller.PublicCreateProblemDetails(title, detail, statusCode, requestId, null) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        var problemDetails = result.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.False(problemDetails.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void CreateProblemDetails_AddsSecurityHeaders()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";
        var statusCode = StatusCodes.Status400BadRequest;
        var requestId = "test-request-id";

        // Act
        var result = _controller.PublicCreateProblemDetails(title, detail, statusCode, requestId);

        // Assert
        Assert.Equal(requestId, _httpContext.Response.Headers["X-Request-ID"]);
        Assert.Equal("nosniff", _httpContext.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", _httpContext.Response.Headers["X-Frame-Options"]);
        Assert.Equal("1; mode=block", _httpContext.Response.Headers["X-XSS-Protection"]);
    }

    #endregion

    #region AddSecurityHeaders Tests

    [Fact]
    public void AddSecurityHeaders_AddsAllRequiredHeaders()
    {
        // Arrange
        var requestId = "test-request-id-123";

        // Act
        _controller.PublicAddSecurityHeaders(requestId);

        // Assert
        Assert.Equal(requestId, _httpContext.Response.Headers["X-Request-ID"]);
        Assert.Equal("nosniff", _httpContext.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", _httpContext.Response.Headers["X-Frame-Options"]);
        Assert.Equal("1; mode=block", _httpContext.Response.Headers["X-XSS-Protection"]);
    }

    [Fact]
    public void AddSecurityHeaders_WithEmptyRequestId_AddsEmptyHeader()
    {
        // Arrange
        var requestId = "";

        // Act
        _controller.PublicAddSecurityHeaders(requestId);

        // Assert
        Assert.Equal(requestId, _httpContext.Response.Headers["X-Request-ID"]);
    }

    #endregion

    #region ApiSuccess Tests

    [Fact]
    public void ApiSuccess_WithData_ReturnsOkResultWithStructuredResponse()
    {
        // Arrange
        var testData = new { Name = "Test", Value = 42 };
        var message = "Operation successful";

        // Act
        var result = _controller.PublicApiSuccess(testData, message) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);

        var response = result.Value;
        Assert.NotNull(response);

        // Use reflection to check anonymous object properties
        var responseType = response.GetType();
        var successProperty = responseType.GetProperty("success");
        var dataProperty = responseType.GetProperty("data");
        var messageProperty = responseType.GetProperty("message");
        var requestIdProperty = responseType.GetProperty("requestId");

        Assert.NotNull(successProperty);
        Assert.NotNull(dataProperty);
        Assert.NotNull(messageProperty);
        Assert.NotNull(requestIdProperty);

        Assert.True((bool)successProperty.GetValue(response)!);
        Assert.Equal(testData, dataProperty.GetValue(response));
        Assert.Equal(message, messageProperty.GetValue(response)!);
        Assert.IsType<string>(requestIdProperty.GetValue(response));
    }

    [Fact]
    public void ApiSuccess_WithNullMessage_SetsMessageToNull()
    {
        // Arrange
        var testData = "test data";

        // Act
        var result = _controller.PublicApiSuccess(testData, null) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        var response = result.Value;
        var responseType = response.GetType();
        var messageProperty = responseType.GetProperty("message");
        Assert.Null(messageProperty?.GetValue(response));
    }

    [Fact]
    public void ApiSuccess_AddsSecurityHeaders()
    {
        // Arrange
        var testData = "test";

        // Act
        var result = _controller.PublicApiSuccess(testData);

        // Assert
        Assert.True(_httpContext.Response.Headers.ContainsKey("X-Request-ID"));
        Assert.Equal("nosniff", _httpContext.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", _httpContext.Response.Headers["X-Frame-Options"]);
        Assert.Equal("1; mode=block", _httpContext.Response.Headers["X-XSS-Protection"]);
    }

    [Fact]
    public void ApiSuccess_WithGenericType_WorksCorrectly()
    {
        // Arrange
        var testList = new List<string> { "item1", "item2", "item3" };

        // Act
        var result = _controller.PublicApiSuccess(testList) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        var response = result.Value;
        var responseType = response.GetType();
        var dataProperty = responseType.GetProperty("data");
        Assert.Equal(testList, dataProperty?.GetValue(response));
    }

    #endregion

    #region ApiError Tests

    [Fact]
    public void ApiError_WithBasicParameters_ReturnsProblemDetailsResponse()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";
        var statusCode = StatusCodes.Status400BadRequest;
        _httpContext.Request.Path = "/api/test";

        // Act
        var result = _controller.PublicApiError(title, detail, statusCode) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(statusCode, result.StatusCode);

        var problemDetails = result.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.Equal(title, problemDetails.Title);
        Assert.Equal(detail, problemDetails.Detail);
        Assert.Equal(statusCode, problemDetails.Status);
        Assert.Equal("/api/test", problemDetails.Instance);
        Assert.True(problemDetails.Extensions.ContainsKey("requestId"));
    }

    [Fact]
    public void ApiError_WithDefaultStatusCode_UsesBadRequest()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";

        // Act
        var result = _controller.PublicApiError(title, detail) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void ApiError_AddsSecurityHeaders()
    {
        // Arrange
        var title = "Test Error";
        var detail = "Test error details";

        // Act
        var result = _controller.PublicApiError(title, detail);

        // Assert
        Assert.True(_httpContext.Response.Headers.ContainsKey("X-Request-ID"));
        Assert.Equal("nosniff", _httpContext.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", _httpContext.Response.Headers["X-Frame-Options"]);
        Assert.Equal("1; mode=block", _httpContext.Response.Headers["X-XSS-Protection"]);
    }

    [Fact]
    public void ApiError_WithDifferentStatusCodes_WorksCorrectly()
    {
        // Arrange
        var testCases = new[]
        {
            (StatusCodes.Status400BadRequest, "Bad Request"),
            (StatusCodes.Status401Unauthorized, "Unauthorized"),
            (StatusCodes.Status403Forbidden, "Forbidden"),
            (StatusCodes.Status404NotFound, "Not Found"),
            (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        foreach (var (statusCode, title) in testCases)
        {
            // Act
            var result = _controller.PublicApiError(title, "Test detail", statusCode) as ObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(statusCode, result.StatusCode);

            var problemDetails = result.Value as ProblemDetails;
            Assert.NotNull(problemDetails);
            Assert.Equal(statusCode, problemDetails.Status);
        }
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FullRequestFlow_IntegratesAllMethodsCorrectly()
    {
        // Arrange
        var testData = new { Result = "Success", Timestamp = DateTime.UtcNow };
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        _httpContext.Request.Headers["User-Agent"] = "TestAgent/1.0";

        // Act - Simulate a complete request flow
        var requestId = _controller.PublicGetRequestId();
        var clientIp = _controller.PublicGetClientIP();
        var userAgent = _controller.PublicGetUserAgent();

        // Simulate successful operation
        var successResult = _controller.PublicApiSuccess(testData, "Operation completed");

        // Assert
        Assert.NotNull(requestId);
        Assert.Equal("10.0.0.1", clientIp);
        Assert.Equal("TestAgent/1.0", userAgent);

        var okResult = successResult as OkObjectResult;
        Assert.NotNull(okResult);

        // Verify security headers were added
        Assert.True(_httpContext.Response.Headers.ContainsKey("X-Request-ID"));
        Assert.Equal("nosniff", _httpContext.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public void ErrorFlow_IntegratesAllMethodsCorrectly()
    {
        // Arrange
        _httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");
        _httpContext.Request.Headers["User-Agent"] = "ErrorClient/1.0";
        _httpContext.Request.Path = "/api/failing-operation";

        // Act - Simulate error flow
        var requestId = _controller.PublicGetRequestId();
        var clientIp = _controller.PublicGetClientIP();
        var userAgent = _controller.PublicGetUserAgent();

        // Simulate error operation
        var errorResult = _controller.PublicApiError("Operation Failed", "Something went wrong", StatusCodes.Status500InternalServerError);

        // Assert
        Assert.NotNull(requestId);
        Assert.Equal("192.168.1.1", clientIp);
        Assert.Equal("ErrorClient/1.0", userAgent);

        var objectResult = errorResult as ObjectResult;
        Assert.NotNull(objectResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);

        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.Equal("Operation Failed", problemDetails.Title);
        Assert.Equal("Something went wrong", problemDetails.Detail);
        Assert.Equal("/api/failing-operation", problemDetails.Instance);

        // Verify security headers were added
        Assert.True(_httpContext.Response.Headers.ContainsKey("X-Request-ID"));
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    public void GetClientIP_WithComplexXForwardedForHeader_ParsesCorrectly()
    {
        // Arrange - Simulate real-world proxy headers with spaces and multiple IPs
        _httpContext.Request.Headers["X-Forwarded-For"] = "  203.0.113.1  ,  198.51.100.1,192.0.2.1  ";

        // Act
        var result = _controller.PublicGetClientIP();

        // Assert
        Assert.Equal("203.0.113.1", result);
    }

    [Fact]
    public void CreateProblemDetails_WithVeryLongStrings_HandlesCorrectly()
    {
        // Arrange
        var longTitle = new string('A', 1000);
        var longDetail = new string('B', 2000);
        var requestId = new string('C', 100);

        // Act
        var result = _controller.PublicCreateProblemDetails(longTitle, longDetail, 400, requestId) as ObjectResult;

        // Assert
        Assert.NotNull(result);
        var problemDetails = result.Value as ProblemDetails;
        Assert.NotNull(problemDetails);
        Assert.Equal(longTitle, problemDetails.Title);
        Assert.Equal(longDetail, problemDetails.Detail);
        Assert.Equal(requestId, problemDetails.Extensions["requestId"]);
    }

    [Fact]
    public void ApiSuccess_WithComplexObject_HandlesCorrectly()
    {
        // Arrange
        var complexObject = new
        {
            Nested = new { Value = 123 },
            List = new List<string> { "a", "b", "c" },
            Date = DateTime.UtcNow,
            Dictionary = new Dictionary<string, int> { ["key1"] = 1, ["key2"] = 2 }
        };

        // Act
        var result = _controller.PublicApiSuccess(complexObject) as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        var response = result.Value;
        var responseType = response.GetType();
        var dataProperty = responseType.GetProperty("data");
        Assert.Equal(complexObject, dataProperty?.GetValue(response));
    }

    #endregion
}
