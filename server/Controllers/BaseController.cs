using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XoHub.Server.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected readonly ILogger _logger;
    protected readonly IConfiguration _configuration;

    public ApiControllerBase(ILogger logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Generates a unique request ID for tracking
    /// </summary>
    protected string GetRequestId() => Guid.NewGuid().ToString();

    /// <summary>
    /// Gets the real client IP address, considering proxy headers
    /// </summary>
    protected string GetClientIP()
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

    /// <summary>
    /// Gets the User-Agent header from the request
    /// </summary>
    protected string GetUserAgent()
    {
        return HttpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown";
    }

    /// <summary>
    /// Creates a standardized ProblemDetails response with security headers
    /// </summary>
    protected IActionResult CreateProblemDetails(string title, string detail, int statusCode, string requestId, List<string>? errors = null)
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
        AddSecurityHeaders(requestId);

        return StatusCode(statusCode, problemDetails);
    }

    /// <summary>
    /// Adds common security headers to the response
    /// </summary>
    protected void AddSecurityHeaders(string requestId)
    {
        Response.Headers["X-Request-ID"] = requestId;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-XSS-Protection"] = "1; mode=block";
    }

    /// <summary>
    /// Creates a standardized success response
    /// </summary>
    protected IActionResult ApiSuccess<T>(T data, string? message = null)
    {
        var requestId = GetRequestId();
        AddSecurityHeaders(requestId);

        var response = new
        {
            success = true,
            data = data,
            message = message,
            requestId = requestId
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates a standardized error response
    /// </summary>
    protected IActionResult ApiError(string title, string detail, int statusCode = StatusCodes.Status400BadRequest)
    {
        var requestId = GetRequestId();
        return CreateProblemDetails(title, detail, statusCode, requestId);
    }
}