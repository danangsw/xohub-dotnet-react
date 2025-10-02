using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using XoHub.Server.Hubs;
using XoHub.Server.Services;

string API_VERSION_PREFIX = "api/v1";
string APP_NAME = "TicTacToe Server";
string APP_VERSION = "1.0.0";
string DEV_URI = "http://localhost:5000";

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// CONFIGURATION & ENVIRONMENT SETUP
// ==========================================

// Load environment-specific configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ==========================================
// LOGGING CONFIGURATION
// ==========================================

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure structured logging with different levels per environment
if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
    builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning);
}

// ==========================================
// SERVICE REGISTRATIONS
// ==========================================

// Core business services (Singletons for shared state)
builder.Services.AddSingleton<IKeyManager, KeyManager>();
builder.Services.AddSingleton<IRoomManager, RoomManager>();
builder.Services.AddSingleton<IAIEngine, AIEngine>();

// Authentication & security services
builder.Services.AddScoped<IAuthService, AuthService>();

// Distributed caching for security features (rate limiting, account lockout)
builder.Services.AddDistributedMemoryCache(); // In production, use Redis/SQL Server

// HTTP Context accessor for JWT key resolution
builder.Services.AddHttpContextAccessor();

// Background services
builder.Services.AddHostedService<RoomPruner>();

// ==========================================
// AUTHENTICATION & AUTHORIZATION
// ==========================================

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JWT:Issuer"] ?? "TicTacToeServer",
            ValidAudience = builder.Configuration["JWT:Audience"] ?? "TicTacToeClient",
            ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        // Dynamic key resolution using IKeyManager - will be configured below

        // SignalR-specific configuration
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Extract token from query string for SignalR connections
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(context.Exception, "JWT authentication failed for {Path}",
                    context.Request.Path);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var userId = context.Principal?.FindFirst("sub")?.Value;
                logger.LogDebug("JWT token validated for user {UserId}", userId);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ==========================================
// CONTROLLERS & API
// ==========================================

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Use PascalCase
        options.JsonSerializerOptions.WriteIndented = builder.Environment.IsDevelopment();
    });

// ==========================================
// SIGNALR CONFIGURATION
// ==========================================

builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.EnableDetailedErrors = builder.Environment.IsDevelopment();
    hubOptions.MaximumReceiveMessageSize = 1024 * 1024; // 1MB max message size
    hubOptions.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(30);
    hubOptions.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

// ==========================================
// CORS CONFIGURATION
// ==========================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Development fallback
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// ==========================================
// SECURITY & MIDDLEWARE
// ==========================================

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

// Rate limiting configuration
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthRateLimit", httpContext =>
    {
        // Get client IP for rate limiting
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ip = forwardedFor.Split(',').First().Trim();
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int>("Security:RateLimiting:AuthRequestsPerWindow", 10),
                Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Security:RateLimiting:AuthWindowMinutes", 15))
            });
    });

    options.AddPolicy("JwksRateLimit", httpContext =>
    {
        // Get client IP for rate limiting
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ip = forwardedFor.Split(',').First().Trim();
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int>("Security:RateLimiting:JwksRequestsPerWindow", 100),
                Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Security:RateLimiting:JwksWindowMinutes", 60))
            });
    });
});

// ==========================================
// HEALTH CHECKS
// ==========================================

builder.Services.AddHealthChecks();

// ==========================================
// BUILD APPLICATION
// ==========================================

var app = builder.Build();

// ==========================================
// CONFIGURE JWT WITH SERVICE PROVIDER
// ==========================================

// Configure JWT key resolution after app is built
var keyManager = app.Services.GetRequiredService<IKeyManager>();
var jwtLogger = app.Services.GetRequiredService<ILogger<Program>>();
var jwtOptions = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

// Get the current JWT Bearer options
var currentOptions = jwtOptions.Get(JwtBearerDefaults.AuthenticationScheme);

jwtLogger.LogInformation("Configuring JWT signing keys...");

// Get keys from KeyManager and set them on the token validation parameters
var jwks = keyManager.GetJwks();
var signingKeys = jwks.Keys.Select(k => new RsaSecurityKey(new RSAParameters
{
    Modulus = Base64UrlEncoder.DecodeBytes(k.N),
    Exponent = Base64UrlEncoder.DecodeBytes(k.E)
})
{ KeyId = k.Kid }).ToList<SecurityKey>();

jwtLogger.LogInformation("Setting {KeyCount} signing keys: {KeyIds}",
    signingKeys.Count, string.Join(", ", signingKeys.Select(k => k.KeyId)));

// Set the signing keys directly
currentOptions.TokenValidationParameters.IssuerSigningKeys = signingKeys;

// ==========================================
// MIDDLEWARE CONFIGURATION (ORDER MATTERS!)
// ==========================================

// Security headers (early in pipeline)
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Comment out for local testing with HTTP
app.UseHttpsRedirection();

// Rate limiting (before CORS and authentication)
app.UseRateLimiter();

// CORS (before authentication)
app.UseCors("AllowFrontend");

// Static files (if needed in future)
app.UseStaticFiles();

// Routing
app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Health checks
app.MapHealthChecks("/health");

// API Controllers
app.MapControllers();

// SignalR Hubs
app.MapHub<TicTacToeHub>("/tictactoehub");

// ==========================================
// GLOBAL ERROR HANDLING
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.MapGet("/error", () => Results.Problem("An error occurred.", statusCode: 500));
}

// ==========================================
// STARTUP LOGGING
// ==========================================

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var serverAddresses = app.Urls; // Get the actual listening URLs

var appName = builder.Configuration["Application:Name"] ?? APP_NAME;
var appVersion = builder.Configuration["Application:Version"] ?? APP_VERSION;
var apiVersion = builder.Configuration["ApiSettings:VersionPrefix"] ?? API_VERSION_PREFIX;

logger.LogInformation("🚀 Starting {AppName} v{AppVersion}", appName, appVersion);
logger.LogInformation("🌍 Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("📡 Listening on:");
foreach (var url in serverAddresses)
{
    logger.LogInformation("   🔗 {Url}", url);
}
logger.LogInformation("🎮 SignalR Hub: {BaseUrl}/tictactoehub", serverAddresses.FirstOrDefault() ?? DEV_URI);
logger.LogInformation("🔐 JWKS Endpoint: {BaseUrl}/.well-known/jwks.json", serverAddresses.FirstOrDefault() ?? DEV_URI);
logger.LogInformation("💚 Health Check: {BaseUrl}/health", serverAddresses.FirstOrDefault() ?? DEV_URI);
logger.LogInformation("🔑 Auth API: {BaseUrl}/{ApiVersion}/auth", serverAddresses.FirstOrDefault() ?? DEV_URI, apiVersion);

// ==========================================
// APPLICATION STARTUP
// ==========================================

app.Run();

// ==========================================
// HELPER METHODS
// ==========================================

