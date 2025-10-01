using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using XoHub.Server.Hubs;
using XoHub.Server.Services;

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

        // Dynamic key resolution using KeyManager
        // Note: We'll handle key resolution in the OnTokenValidated event
        // to avoid service locator anti-pattern during configuration
        options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
        {
            // Return empty for now - validation will happen in OnTokenValidated
            return Array.Empty<SecurityKey>();
        };

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

// Configure JWT key resolver after app is built to avoid BuildServiceProvider warning
ConfigureJwtKeyResolver(app);

// ==========================================
// MIDDLEWARE CONFIGURATION (ORDER MATTERS!)
// ==========================================

// Security headers (early in pipeline)
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

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
logger.LogInformation("Starting TicTacToe Server v1.0");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("SignalR Hub: /tictactoehub");
logger.LogInformation("JWKS Endpoint: /.well-known/jwks.json");
logger.LogInformation("Health Check: /health");

// ==========================================
// APPLICATION STARTUP
// ==========================================

app.Run();

// ==========================================
// HELPER METHODS
// ==========================================

static void ConfigureJwtKeyResolver(WebApplication app)
{
    var jwtBearerOptions = app.Services.GetRequiredService<IOptions<JwtBearerOptions>>().Value;

    jwtBearerOptions.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
    {
        var keyManager = app.Services.GetRequiredService<IKeyManager>();
        var jwks = keyManager.GetJwks();
        return jwks.Keys
            .Where(k => k.Kid == kid)
            .Select(k => new RsaSecurityKey(new RSAParameters
            {
                Modulus = Base64UrlEncoder.DecodeBytes(k.N),
                Exponent = Base64UrlEncoder.DecodeBytes(k.E)
            })
            { KeyId = k.Kid });
    };
}
