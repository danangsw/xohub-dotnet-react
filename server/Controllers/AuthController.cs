using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using XoHub.Server.Models;
using XoHub.Server.Services;

namespace XoHub.Server.Controllers;

[ApiController]
[Route("{versionPrefix}/[controller]")] // Dynamic route prefix from configuration
[EnableRateLimiting("AuthRateLimit")]
[Produces("application/json")]
public class AuthController : ApiControllerBase
{
    private readonly IKeyManager _keyManager;
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    // Security constants
    private const int MAX_REQUEST_SIZE_BYTES = 4096; // 4KB max request size
    private const string ALLOWED_CONTENT_TYPE = "application/json";

    public AuthController(
        IKeyManager keyManager,
        IAuthService authService,
        ILogger<AuthController> logger,
        IConfiguration configuration) : base(configuration)
    {
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var requestId = Guid.NewGuid().ToString();
        var clientIP = GetClientIP();
        var userAgent = GetUserAgent();

        try
        {
            // Step 1: Pre-flight security checks
            var validationResult = PerformSecurityValidationAsync(request, clientIP, requestId);
            if (validationResult != null)
            {
                return validationResult;
            }

            // Step 2: Rate limiting check
            if (await _authService.IsRateLimitExceededAsync(clientIP, "login"))
            {
                _logger.LogWarning("Rate limit exceeded for login attempt from IP: {IP}, RequestId: {RequestId}",
                    clientIP, requestId);
                return CreateProblemDetails(
                    "Too many requests",
                    "Rate limit exceeded. Please try again later.",
                    StatusCodes.Status429TooManyRequests,
                    requestId);
            }

            // Step 3: Record the request for rate limiting
            await _authService.RecordRequestAsync(clientIP, "login");

            // Step 4: Authenticate user
            var (success, userId, errorMessage) = await _authService.AuthenticateUserAsync(
                request.UserName, request.Password);

            if (!success)
            {
                _logger.LogInformation("Failed login attempt for user: {UserName} from IP: {IP}, Reason: {Reason}, RequestId: {RequestId}",
                    request.UserName, clientIP, errorMessage, requestId);

                return CreateProblemDetails(
                    "Authentication failed",
                    errorMessage ?? "Invalid credentials",
                    StatusCodes.Status401Unauthorized,
                    requestId);
            }

            // Step 5: Generate JWT token
            var token = _keyManager.GenerateJwtToken(userId!, request.UserName);

            // Step 6: Log successful authentication (without sensitive data)
            _logger.LogInformation("Successful login for user: {UserName} from IP: {IP}, UserId: {UserId}, RequestId: {RequestId}",
                request.UserName, clientIP, userId, requestId);

            // Step 7: Return response
            var response = new LoginResponse
            {
                Token = token,
                UserId = userId!,
                ExpiresIn = 3600, // 1 hour in seconds
                TokenType = "Bearer"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for user: {UserName} from IP: {IP}, RequestId: {RequestId}",
                request?.UserName ?? "unknown", clientIP, requestId);

            return CreateProblemDetails(
                "Internal server error",
                "An unexpected error occurred. Please try again later.",
                StatusCodes.Status500InternalServerError,
                requestId);
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        var requestId = Guid.NewGuid().ToString();
        var clientIP = GetClientIP();
        var userId = User.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            // In JWT, logout is typically handled client-side by discarding the token
            // Server-side token revocation would require a token blacklist (not implemented here)

            _logger.LogInformation("User logout: {UserId} from IP: {IP}, RequestId: {RequestId}",
                userId, clientIP, requestId);

            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout for user: {UserId} from IP: {IP}, RequestId: {RequestId}",
                userId, clientIP, requestId);

            return CreateProblemDetails(
                "Internal server error",
                "An error occurred during logout.",
                StatusCodes.Status500InternalServerError,
                requestId);
        }
    }

    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType(typeof(UserStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult GetStatus()
    {
        var requestId = Guid.NewGuid().ToString();
        var clientIP = GetClientIP();
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var userName = User.FindFirst("username")?.Value ?? "unknown";

        try
        {
            var response = new UserStatusResponse
            {
                UserId = userId,
                UserName = userName,
                IsAuthenticated = true,
                LastActivity = DateTime.UtcNow
            };

            _logger.LogDebug("User status requested: {UserId} from IP: {IP}, RequestId: {RequestId}",
                userId, clientIP, requestId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user status for user: {UserId} from IP: {IP}, RequestId: {RequestId}",
                userId, clientIP, requestId);

            return CreateProblemDetails(
                "Internal server error",
                "Unable to retrieve user status.",
                StatusCodes.Status500InternalServerError,
                requestId);
        }
    }

    #region Security Validation Methods

    private IActionResult? PerformSecurityValidationAsync(LoginRequest request, string clientIP, string requestId)
    {
        // 1. Content-Type validation
        if (!Request.ContentType?.StartsWith(ALLOWED_CONTENT_TYPE, StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogWarning("Invalid content type: {ContentType} from IP: {IP}, RequestId: {RequestId}",
                Request.ContentType, clientIP, requestId);
            return CreateProblemDetails(
                "Invalid request",
                "Content-Type must be application/json",
                StatusCodes.Status400BadRequest,
                requestId);
        }

        // 2. Request size validation
        if (Request.ContentLength > MAX_REQUEST_SIZE_BYTES)
        {
            _logger.LogWarning("Request too large: {Size} bytes from IP: {IP}, RequestId: {RequestId}",
                Request.ContentLength, clientIP, requestId);
            return CreateProblemDetails(
                "Request too large",
                $"Request body must be less than {MAX_REQUEST_SIZE_BYTES} bytes",
                StatusCodes.Status400BadRequest,
                requestId);
        }

        // 3. Model validation
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            _logger.LogWarning("Model validation failed: {Errors} from IP: {IP}, RequestId: {RequestId}",
                string.Join(", ", errors), clientIP, requestId);

            return CreateProblemDetails(
                "Validation failed",
                "Please check your input data",
                StatusCodes.Status400BadRequest,
                requestId,
                errors);
        }

        // 4. Enhanced input validation
        if (!IsValidUserName(request.UserName))
        {
            _logger.LogWarning("Invalid username format: {UserName} from IP: {IP}, RequestId: {RequestId}",
                request.UserName, clientIP, requestId);
            return CreateProblemDetails(
                "Invalid input",
                "Username contains invalid characters",
                StatusCodes.Status400BadRequest,
                requestId);
        }

        if (!IsValidPassword(request.Password))
        {
            _logger.LogWarning("Invalid password format from IP: {IP}, RequestId: {RequestId}",
                clientIP, requestId);
            return CreateProblemDetails(
                "Invalid input",
                "Password does not meet security requirements",
                StatusCodes.Status400BadRequest,
                requestId);
        }

        return null; // Validation passed
    }

    private bool IsValidUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return false;

        // Allow only alphanumeric characters and underscores
        // Length between 3-50 characters
        return Regex.IsMatch(userName, @"^[a-zA-Z0-9_]{3,50}$");
    }

    private bool IsValidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return false;

        // Require at least one uppercase, one lowercase, one digit, one special character
        return Regex.IsMatch(password,
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$");
    }

    private IActionResult CreateProblemDetails(string title, string detail, int statusCode, string requestId, List<string>? errors = null)
    {
        var problemDetails = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode,
            Instance = Request.Path,
            Extensions = { ["requestId"] = requestId }
        };

        if (errors != null && errors.Any())
        {
            problemDetails.Extensions["errors"] = errors;
        }

        // Add security headers
        Response.Headers["X-Request-ID"] = requestId;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-XSS-Protection"] = "1; mode=block";

        return StatusCode(statusCode, problemDetails);
    }

    private string GetClientIP()
    {
        // Get real client IP, considering proxy headers
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Check for forwarded headers (use with caution in production)
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ip = forwardedFor.Split(',').First().Trim();
        }

        var forwarded = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            ip = forwarded;
        }

        return ip ?? "unknown";
    }

    private string GetUserAgent()
    {
        return HttpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown";
    }

    #endregion
}

// Enhanced response models
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
}

public class UserStatusResponse
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public DateTime LastActivity { get; set; }
}