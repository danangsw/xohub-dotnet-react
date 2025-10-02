using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using XoHub.Server.Models;

namespace XoHub.Server.Services;

public interface IAuthService
{
    Task<(bool Success, string? UserId, string? ErrorMessage)> AuthenticateUserAsync(string userName, string password);
    Task<bool> IsAccountLockedAsync(string userName);
    Task RecordFailedLoginAttemptAsync(string userName);
    Task ResetFailedLoginAttemptsAsync(string userName);
    Task<bool> IsRateLimitExceededAsync(string identifier, string action);
    Task RecordRequestAsync(string identifier, string action);
}

public class AuthService : IAuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly ICacheWrapper _cache;
    private readonly IConfigurationService _configurationService;

    // Security configuration
    private readonly int _maxFailedLoginAttempts;
    private readonly TimeSpan _accountLockoutDuration;
    private readonly int _rateLimitMaxRequests;
    private readonly TimeSpan _rateLimitWindow;
    private readonly TimeSpan _failedLoginTrackingWindow;

    public AuthService(
        ILogger<AuthService> logger,
        ICacheWrapper cache,
        IConfigurationService configurationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));

        // Load security configuration with secure defaults
        _maxFailedLoginAttempts = _configurationService.GetMaxFailedLoginAttempts();
        _accountLockoutDuration = TimeSpan.FromMinutes(_configurationService.GetAccountLockoutMinutes());
        _rateLimitMaxRequests = _configurationService.GetRateLimitMaxRequests();
        _rateLimitWindow = TimeSpan.FromMinutes(_configurationService.GetRateLimitWindowMinutes());
        _failedLoginTrackingWindow = TimeSpan.FromHours(_configurationService.GetFailedLoginTrackingHours());
    }

    public async Task<(bool Success, string? UserId, string? ErrorMessage)> AuthenticateUserAsync(string userName, string password)
    {
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Authentication attempt with empty credentials");
                return (false, null, "Invalid credentials");
            }

            // Check account lockout status
            if (await IsAccountLockedAsync(userName))
            {
                _logger.LogWarning("Authentication attempt for locked account: {UserName}",
                    userName);
                return (false, null, "Account temporarily locked due to multiple failed attempts");
            }

            // Validate credentials against user store
            var (isValid, userId) = ValidateCredentialsAsync(userName, password);

            if (isValid && userId != null)
            {
                // Successful authentication
                await ResetFailedLoginAttemptsAsync(userName);
                _logger.LogInformation("Successful authentication for user: {UserName}",
                    userName);
                return (true, userId, null);
            }
            else
            {
                // Failed authentication
                await RecordFailedLoginAttemptAsync(userName);
                _logger.LogWarning("Failed authentication attempt for user: {UserName}",
                    userName);
                return (false, null, "Invalid credentials");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during authentication for user: {UserName}", userName);
            return (false, null, "Authentication service temporarily unavailable");
        }
    }

    public async Task<bool> IsAccountLockedAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return false;

        var cacheKey = $"account_lockout:{userName.ToLowerInvariant()}";
        var lockoutData = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(lockoutData))
            return false;

        try
        {
            var lockoutInfo = System.Text.Json.JsonSerializer.Deserialize<AccountLockoutInfo>(lockoutData);
            if (lockoutInfo == null)
                return false;

            // Check if lockout has expired
            if (DateTime.UtcNow > lockoutInfo.LockoutUntil)
            {
                await _cache.RemoveAsync(cacheKey);
                return false;
            }

            return true;
        }
        catch
        {
            // If deserialization fails, remove the cache entry and allow login
            await _cache.RemoveAsync(cacheKey);
            return false;
        }
    }

    public async Task RecordFailedLoginAttemptAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        var userNameKey = userName.ToLowerInvariant();
        var attemptsKey = $"failed_attempts:{userNameKey}";
        var attemptsData = await _cache.GetStringAsync(attemptsKey);

        var attempts = 0;
        if (!string.IsNullOrEmpty(attemptsData))
        {
            try
            {
                var attemptsInfo = System.Text.Json.JsonSerializer.Deserialize<FailedAttemptsInfo>(attemptsData);
                if (attemptsInfo != null && DateTime.UtcNow < attemptsInfo.ResetTime)
                {
                    attempts = attemptsInfo.Count;
                }
            }
            catch
            {
                // Reset on deserialization error
            }
        }

        attempts++;

        var newAttemptsInfo = new FailedAttemptsInfo
        {
            Count = attempts,
            ResetTime = DateTime.UtcNow.Add(_failedLoginTrackingWindow)
        };

        await _cache.SetStringAsync(attemptsKey,
            System.Text.Json.JsonSerializer.Serialize(newAttemptsInfo),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _failedLoginTrackingWindow
            });

        // Check if account should be locked
        if (attempts >= _maxFailedLoginAttempts)
        {
            var lockoutInfo = new AccountLockoutInfo
            {
                UserName = userName,
                FailedAttempts = attempts,
                LockoutUntil = DateTime.UtcNow.Add(_accountLockoutDuration),
                LockedAt = DateTime.UtcNow
            };

            var lockoutKey = $"account_lockout:{userNameKey}";
            await _cache.SetStringAsync(lockoutKey,
                System.Text.Json.JsonSerializer.Serialize(lockoutInfo),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _accountLockoutDuration
                });

            _logger.LogWarning("Account locked due to {Attempts} failed login attempts: {UserName}",
                attempts, userName);
        }
    }

    public async Task ResetFailedLoginAttemptsAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        var attemptsKey = $"failed_attempts:{userName.ToLowerInvariant()}";
        await _cache.RemoveAsync(attemptsKey);

        var lockoutKey = $"account_lockout:{userName.ToLowerInvariant()}";
        await _cache.RemoveAsync(lockoutKey);
    }

    public async Task<bool> IsRateLimitExceededAsync(string identifier, string action)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(action))
            return false;

        var cacheKey = $"rate_limit:{action}:{identifier}";
        var requestData = await _cache.GetStringAsync(cacheKey);

        var requestCount = 0;
        if (!string.IsNullOrEmpty(requestData))
        {
            try
            {
                var rateLimitInfo = System.Text.Json.JsonSerializer.Deserialize<RateLimitInfo>(requestData);
                if (rateLimitInfo != null && DateTime.UtcNow < rateLimitInfo.ResetTime)
                {
                    requestCount = rateLimitInfo.Count;
                }
            }
            catch
            {
                // Reset on deserialization error
            }
        }

        return requestCount >= _rateLimitMaxRequests;
    }

    public async Task RecordRequestAsync(string identifier, string action)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(action))
            return;

        var cacheKey = $"rate_limit:{action}:{identifier}";
        var requestData = await _cache.GetStringAsync(cacheKey);

        var requestCount = 0;
        if (!string.IsNullOrEmpty(requestData))
        {
            try
            {
                var rateLimitInfo = System.Text.Json.JsonSerializer.Deserialize<RateLimitInfo>(requestData);
                if (rateLimitInfo != null && DateTime.UtcNow < rateLimitInfo.ResetTime)
                {
                    requestCount = rateLimitInfo.Count;
                }
            }
            catch
            {
                // Reset on deserialization error
            }
        }

        requestCount++;

        var newRateLimitInfo = new RateLimitInfo
        {
            Count = requestCount,
            ResetTime = DateTime.UtcNow.Add(_rateLimitWindow),
            Action = action,
            Identifier = identifier
        };

        await _cache.SetStringAsync(cacheKey,
            System.Text.Json.JsonSerializer.Serialize(newRateLimitInfo),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _rateLimitWindow
            });
    }

    private (bool IsValid, string? UserId) ValidateCredentialsAsync(string userName, string password)
    {
        // For demonstration purposes, using in-memory user store
        // In production, this should validate against a secure database
        var users = new Dictionary<string, (string PasswordHash, string Salt, string UserId)>
        {
            ["admin"] = (HashPassword("AdminPass123!", "ZGVmYXVsdF9zYWx0"), "ZGVmYXVsdF9zYWx0", "user_admin_001"),
            ["player1"] = (HashPassword("Player123!", "cGxheWVyMV9zYWx0"), "cGxheWVyMV9zYWx0", "user_player1_002"),
            ["player2"] = (HashPassword("Player456!", "cGxheWVyMl9zYWx0"), "cGxheWVyMl9zYWx0", "user_player2_003"),
            ["testuser"] = (HashPassword("TestPass123!", "dGVzdF9zYWx0"), "dGVzdF9zYWx0", "user_test_004")
        };

        if (users.TryGetValue(userName.ToLowerInvariant(), out var userData))
        {
            var (storedHash, salt, userId) = userData;
            var inputHash = HashPassword(password, salt);

            // Use constant-time comparison to prevent timing attacks
            return (CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(storedHash),
                Convert.FromBase64String(inputHash)), userId);
        }

        return (false, null);
    }

    private string HashPassword(string password, string salt)
    {
        // Use PBKDF2 for password hashing (production-ready)
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            Convert.FromBase64String(salt), // Salt is base64 encoded
            10000, // Iterations
            HashAlgorithmName.SHA256);

        var hash = pbkdf2.GetBytes(32); // 256-bit hash
        return Convert.ToBase64String(hash);
    }

    // Cache data models
    private class AccountLockoutInfo
    {
        public string UserName { get; set; } = string.Empty;
        public int FailedAttempts { get; set; }
        public DateTime LockoutUntil { get; set; }
        public DateTime LockedAt { get; set; }
    }

    private class FailedAttemptsInfo
    {
        public int Count { get; set; }
        public DateTime ResetTime { get; set; }
    }

    private class RateLimitInfo
    {
        public int Count { get; set; }
        public DateTime ResetTime { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
    }
}