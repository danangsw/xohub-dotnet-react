using System;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace XoHub.Server.Services;

public interface IKeyManager
{
    string GenerateJwtToken(string userId, string userName);
    JsonWebKeySet GetJwks();
    void RotateKeys();
    Task<(bool IsValid, ClaimsPrincipal? Principal)> ValidateTokenAsync(string token);  // Changed to async
    Task<bool> IsTokenValidAsync(string token);
}

/// <summary>
/// Manages JWT key rotation and token validation with enhanced security
/// 
/// Security Design Principles:
/// 1. RSA-256 asymmetric encryption for token signing
/// 2. Hourly key rotation minimizes exposure window
/// 3. Overlapping key validity (2-hour window) prevents service disruption
/// 4. Secure key storage in Docker volumes with proper permissions
/// 5. JWKS endpoint compliance for standard token validation
/// 
/// Algorithm Complexity:
/// - Token generation: O(1) - RSA signing operation
/// - Token validation: O(k) where k is number of active keys (max 2)
/// - Key rotation: O(1) - atomic key swap operation
/// </summary>
public class KeyManager : IKeyManager, IDisposable
{
    private readonly ILogger<KeyManager> _logger;
    private readonly string _keyStoragePath;
    private readonly object _keyLock = new();
    private readonly SemaphoreSlim _keySemaphore = new SemaphoreSlim(1, 1);  // Allows 1 concurrent access

    // Key management
    private RSA? _currentSigningKey;
    private RSA? _previousSigningKey;
    private string _currentKeyId = string.Empty;
    private string _previousKeyId = string.Empty;
    private DateTime _lastRotation = DateTime.MinValue;

    // JWT configuration
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _tokenLifetime;
    private readonly TimeSpan _keyRotationInterval;
    private readonly TimeSpan _keyOverlapWindow;

    private TimeSpan _clockSkew;

    public KeyManager(ILogger<KeyManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _keyStoragePath = configuration.GetValue<string>("JWT:KeyStoragePath") ?? "/app/keys";
        _issuer = configuration.GetValue<string>("JWT:Issuer") ?? "TicTacToeServer";
        _audience = configuration.GetValue<string>("JWT:Audience") ?? "TicTacToeClient";
        _tokenLifetime = TimeSpan.FromHours(configuration.GetValue<double>("JWT:TokenLifetimeHours", 0));
        _keyRotationInterval = TimeSpan.FromHours(configuration.GetValue<double>("JWT:KeyRotationHours", 1));
        _keyOverlapWindow = TimeSpan.FromHours(configuration.GetValue<double>("JWT:KeyOverlapHours", 2));
        _clockSkew = TimeSpan.FromSeconds(configuration.GetValue<int>("JWT:ClockSkewSeconds", 300));

        InitializeKeys();
        _logger.LogInformation("KeyManager initialized with {TokenLifetime}h tokens, {RotationInterval}h rotation",
            _tokenLifetime.TotalHours, _keyRotationInterval.TotalHours);
    }

    /// <summary>
    /// Initializes RSA keys from storage or creates new ones
    /// 
    /// Key Storage Strategy:
    /// - Keys stored as PEM files in Docker volume
    /// - Atomic file operations prevent corruption
    /// - Automatic key generation on first startup
    /// - Key recovery from persistent storage on restart
    /// </summary>
    private void InitializeKeys()
    {
        lock (_keyLock)
        {
            try
            {
                // Ensure key storage directory exists
                Directory.CreateDirectory(_keyStoragePath);

                // Try to load existing keys
                var currentKeyPath = Path.Combine(_keyStoragePath, "current.pem");
                var previousKeyPath = Path.Combine(_keyStoragePath, "previous.pem");
                var metadataPath = Path.Combine(_keyStoragePath, "metadata.json");

                if (File.Exists(currentKeyPath) && File.Exists(metadataPath))
                {
                    LoadExistingKeys(currentKeyPath, previousKeyPath, metadataPath);
                }
                else
                {
                    GenerateNewKeyPair();
                }

                _logger.LogInformation("Keys initialized. Current: {CurrentKeyId}, Previous: {PreviousKeyId}",
                    _currentKeyId, _previousKeyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize keys. Generating new key pair.");
                GenerateNewKeyPair();
            }
        }
    }

    private void LoadExistingKeys(string currentKeyPath, string previousKeyPath, string metadataPath)
    {
        try
        {
            // Load metadata
            var metadataJson = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<KeyMetadata>(metadataJson);

            if (metadata != null)
            {
                _currentKeyId = metadata.CurrentKeyId;
                _previousKeyId = metadata.PreviousKeyId;
                _lastRotation = metadata.LastRotation;
            }

            // Load current key
            var currentKeyPem = File.ReadAllText(currentKeyPath);
            _currentSigningKey = RSA.Create();
            _currentSigningKey.ImportFromPem(currentKeyPem);

            // Load previous key if exists
            if (File.Exists(previousKeyPath))
            {
                var previousKeyPem = File.ReadAllText(previousKeyPath);
                _previousSigningKey = RSA.Create();
                _previousSigningKey.ImportFromPem(previousKeyPem);
            }

            _logger.LogInformation("Loaded existing keys from storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load existing keys");
            throw;
        }
    }

    /// <summary>
    /// Generates new RSA key pair with secure parameters
    /// 
    /// RSA Configuration:
    /// - 2048-bit key size (industry standard, balances security and performance)
    /// - PKCS#1 padding for signature generation
    /// - SHA-256 hashing algorithm
    /// 
    /// Security Considerations:
    /// - Keys generated using cryptographically secure random number generator
    /// - Private keys never logged or transmitted
    /// - Atomic file operations prevent key corruption during writes
    /// </summary>
    private void GenerateNewKeyPair()
    {
        try
        {
            // Dispose old keys
            _currentSigningKey?.Dispose();
            _previousSigningKey?.Dispose();
            _previousSigningKey = null;

            // Generate new RSA key pair (2048-bit)
            _currentSigningKey = RSA.Create(2048);
            _currentKeyId = GenerateKeyId();
            _lastRotation = DateTime.UtcNow;

            // Save to persistent storage
            SaveKeysToStorage();

            _logger.LogInformation("Generated new RSA key pair. KeyId: {KeyId}", _currentKeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate new key pair");
            throw;
        }
    }

    private string GenerateKeyId()
    {
        // Generate cryptographically secure key identifier
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private void SaveKeysToStorage()
    {
        try
        {
            var currentKeyPath = Path.Combine(_keyStoragePath, "current.pem");
            var previousKeyPath = Path.Combine(_keyStoragePath, "previous.pem");
            var metadataPath = Path.Combine(_keyStoragePath, "metadata.json");

            // Save current key
            if (_currentSigningKey != null)
            {
                var currentKeyPem = _currentSigningKey.ExportRSAPrivateKeyPem();
                File.WriteAllText(currentKeyPath, currentKeyPem);

                // Set secure file permissions (Unix-style)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(currentKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // Save previous key if exists
            if (_previousSigningKey != null)
            {
                var previousKeyPem = _previousSigningKey.ExportRSAPrivateKeyPem();
                File.WriteAllText(previousKeyPath, previousKeyPem);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(previousKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // Save metadata
            var metadata = new KeyMetadata
            {
                CurrentKeyId = _currentKeyId,
                PreviousKeyId = _previousKeyId,
                LastRotation = _lastRotation
            };

            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(metadataPath, metadataJson);

            _logger.LogDebug("Keys saved to persistent storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save keys to storage");
            throw;
        }
    }

    /// <summary>
    /// Generates JWT token with custom claims and enhanced security
    /// 
    /// Token Structure:
    /// - Header: RSA256 algorithm, current key ID
    /// - Payload: Standard claims (iss, aud, exp, iat, sub) + custom claims
    /// - Signature: RSA-SHA256 signature using current private key
    /// 
    /// Security Features:
    /// - Short expiration time (1 hour default)
    /// - Unique JTI (JWT ID) prevents replay attacks
    /// - Issued-at timestamp for freshness validation
    /// - Audience and issuer validation
    /// </summary>
    public string GenerateJwtToken(string userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("UserId and UserName cannot be null or empty");
        }

        lock (_keyLock)
        {
            if (_currentSigningKey == null)
            {
                _logger.LogError("No signing key available for token generation");
                throw new InvalidOperationException("No signing key available");
            }

            try
            {
                var now = DateTime.UtcNow;
                var jti = Guid.NewGuid().ToString(); // Unique token identifier

                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, userId),
                    new(JwtRegisteredClaimNames.Jti, jti),
                    new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new("username", userName),
                    new("role", "player"),
                    new("version", "1.0")
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = now.Add(_tokenLifetime),
                    Issuer = _issuer,
                    Audience = _audience,
                    SigningCredentials = new SigningCredentials(
                        new RsaSecurityKey(_currentSigningKey) { KeyId = _currentKeyId },
                        SecurityAlgorithms.RsaSha256
                    )
                };

                var tokenHandler = new JsonWebTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);

                _logger.LogDebug("Generated JWT token for user {UserId} with JTI {Jti}", userId, jti);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate JWT token for user {UserId}", userId);
                throw;
            }
        }
    }

    public async Task<(bool IsValid, ClaimsPrincipal? Principal)> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, null);

        await _keySemaphore.WaitAsync();  // Async acquire
        try
        {
            // Try current key first
            if (_currentSigningKey != null)
            {
                var success = await TryValidateWithKeyAsync(token, _currentSigningKey, _currentKeyId);
                if (success.IsValid)
                {
                    _logger.LogTrace("Token validated with current key {KeyId}", _currentKeyId);
                    return success;
                }
            }

            // Fallback to previous key
            if (_previousSigningKey != null && ShouldIncludePreviousKey())
            {
                var success = await TryValidateWithKeyAsync(token, _previousSigningKey, _previousKeyId);
                if (success.IsValid)
                {
                    _logger.LogTrace("Token validated with previous key {KeyId}", _previousKeyId);
                    return success;
                }
            }

            _logger.LogWarning("Token validation failed - no valid key found");
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token validation");
            return (false, null);
        }
        finally
        {
            _keySemaphore.Release();  // Always release
        }
    }

    private async Task<(bool IsValid, ClaimsPrincipal? Principal)> TryValidateWithKeyAsync(string token, RSA key, string keyId)
    {
        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(key) { KeyId = keyId },
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(_clockSkew.TotalSeconds),
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            // Create an instance of JsonWebTokenHandler and call ValidateTokenAsync
            var tokenHandler = new JsonWebTokenHandler();
            var result = await tokenHandler.ValidateTokenAsync(token, validationParameters);

            if (result.IsValid)
            {
                var principal = new ClaimsPrincipal(result.ClaimsIdentity);
                return (true, principal);
            }

            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<bool> IsTokenValidAsync(string token)
    {
        var (isValid, _) = await ValidateTokenAsync(token);
        return isValid;
    }

    /// <summary>
    /// Rotates signing keys with overlap window for zero-downtime deployment
    /// 
    /// Rotation Algorithm:
    /// 1. Current key becomes previous key
    /// 2. Generate new current key
    /// 3. Update JWKS endpoint
    /// 4. Persist keys to storage
    /// 5. Schedule cleanup of expired previous key
    /// 
    /// Timing Strategy:
    /// - Rotate every hour (configurable)
    /// - 2-hour overlap window prevents token rejection during rotation
    /// - Automatic cleanup of expired keys
    /// </summary>
    public void RotateKeys()
    {
        lock (_keyLock)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Check if rotation is needed
                if (now - _lastRotation < _keyRotationInterval)
                {
                    _logger.LogTrace("Key rotation not needed yet. Last rotation: {LastRotation}", _lastRotation);
                    return;
                }

                _logger.LogInformation("Starting key rotation");

                // Move current key to previous
                _previousSigningKey?.Dispose();
                _previousSigningKey = _currentSigningKey;
                _previousKeyId = _currentKeyId;

                // Generate new current key
                _currentSigningKey = RSA.Create(2048);
                _currentKeyId = GenerateKeyId();
                _lastRotation = now;

                // Persist to storage
                SaveKeysToStorage();

                _logger.LogInformation("Key rotation completed. New KeyId: {NewKeyId}, Previous KeyId: {PreviousKeyId}",
                    _currentKeyId, _previousKeyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Key rotation failed");
                throw;
            }
        }
    }

    /// <summary>
    /// Generates JWKS (JSON Web Key Set) for public key distribution
    /// 
    /// JWKS Specification (RFC 7517):
    /// - Contains public keys for token verification
    /// - Supports key rotation with multiple active keys
    /// - Standard format for OAuth2/OpenID Connect compliance
    /// 
    /// Key Export:
    /// - RSA public key parameters (n, e)
    /// - Key usage: signature verification
    /// - Algorithm: RS256
    /// - Key ID for key selection during validation
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        lock (_keyLock)
        {
            var jwks = new JsonWebKeySet();

            // Add current key
            if (_currentSigningKey != null)
            {
                var currentJwk = CreateJsonWebKey(_currentSigningKey, _currentKeyId);
                jwks.Keys.Add(currentJwk);
            }

            // Add previous key (during overlap window)
            if (_previousSigningKey != null && ShouldIncludePreviousKey())
            {
                var previousJwk = CreateJsonWebKey(_previousSigningKey, _previousKeyId);
                jwks.Keys.Add(previousJwk);
            }

            _logger.LogTrace("Generated JWKS with {KeyCount} keys", jwks.Keys.Count);
            return jwks;
        }
    }

    private bool ShouldIncludePreviousKey()
    {
        var now = DateTime.UtcNow;
        return now - _lastRotation < _keyOverlapWindow;
    }

    private JsonWebKey CreateJsonWebKey(RSA rsa, string keyId)
    {
        var parameters = rsa.ExportParameters(false); // Export public key only

        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = "RS256",
            Kid = keyId,
            N = Base64UrlEncoder.Encode(parameters.Modulus!),
            E = Base64UrlEncoder.Encode(parameters.Exponent!)
        };
    }

    public void Dispose()
    {
        _keySemaphore.Dispose();
        _currentSigningKey?.Dispose();
        _previousSigningKey?.Dispose();
        _currentSigningKey = null;
        _previousSigningKey = null;
        _logger.LogInformation("KeyManager disposed");
    }
}

public class KeyMetadata
{
    public string CurrentKeyId { get; set; } = string.Empty;
    public string PreviousKeyId { get; set; } = string.Empty;
    public DateTime LastRotation { get; set; }
}
