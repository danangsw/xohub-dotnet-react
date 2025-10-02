using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using XoHub.Server.Services;

namespace XoHub.Server.Controllers;

/// <summary>
/// Controller for JWKS (JSON Web Key Set) endpoint.
/// This endpoint provides the public keys used for JWT validation.
/// </summary>
[ApiController]
[Route(".well-known")]
[EnableRateLimiting("JWKS")]
public class JwksController : ControllerBase
{
    private readonly IKeyManager _keyManager;
    private readonly IDistributedCache _cache;
    private readonly ILogger<JwksController> _logger;

    public JwksController(
        IKeyManager keyManager,
        IDistributedCache cache,
        ILogger<JwksController> logger)
    {
        _keyManager = keyManager;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Gets the JSON Web Key Set containing the public keys for JWT validation.
    /// This endpoint is rate-limited to prevent abuse.
    /// </summary>
    /// <returns>The JWKS response containing the public keys.</returns>
    [HttpGet("jwks.json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(429)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetJwks()
    {
        try
        {
            // Check cache first for performance
            var cacheKey = "jwks_response";
            var cachedResponse = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedResponse))
            {
                _logger.LogDebug("Returning cached JWKS response");
                return Content(cachedResponse, "application/json");
            }

            // Generate JWKS response
            var jwks = _keyManager.GetJwks();

            // Cache the response for 5 minutes (keys rotate hourly, so cache shorter)
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            };

            var jsonResponse = JsonSerializer.Serialize(jwks, new JsonSerializerOptions
            {
                WriteIndented = false // Compact JSON for production
            });

            await _cache.SetStringAsync(cacheKey, jsonResponse, cacheOptions);

            _logger.LogInformation("Generated and cached JWKS response with {KeyCount} keys",
                jwks.Keys?.Count ?? 0);

            return Content(jsonResponse, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating JWKS response");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}