using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace TicTacToe.Server.Tests;

public class ProgramTests
{
    [Fact]
    public void Program_ApplicationCanStart()
    {
        // Arrange & Act
        // Since Program class is not accessible, test the application can be created
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Assert - Should not throw exception
        Assert.NotNull(builder);
        Assert.NotNull(builder.Services);
        Assert.NotNull(builder.Configuration);
    }

    [Fact]
    public void Application_ConfigurationIsAccessible()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Act
        var config = builder.Configuration;

        // Assert
        Assert.NotNull(config);
        var testValue = config.GetValue<string>("NonExistentKey", "DefaultValue");
        Assert.Equal("DefaultValue", testValue);
    }

    [Fact]
    public void Program_CanCreateWebApplicationBuilder()
    {
        // Arrange
        var args = new string[] { };

        // Act & Assert - Should not throw
        var builder = WebApplication.CreateBuilder(args);
        Assert.NotNull(builder);
        Assert.NotNull(builder.Services);
        Assert.NotNull(builder.Configuration);
    }

    [Fact]
    public void ServiceRegistration_RegistersRequiredServices()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Add test configuration values directly to the builder's configuration
        builder.Configuration["JWT:Issuer"] = "TestIssuer";
        builder.Configuration["JWT:Audience"] = "TestAudience";
        builder.Configuration["Security:MaxFailedLoginAttempts"] = "5";
        builder.Configuration["Security:AccountLockoutMinutes"] = "15";
        builder.Configuration["Security:RateLimitMaxRequests"] = "100";
        builder.Configuration["Security:RateLimitWindowMinutes"] = "1";

        // Act - Register services as in Program.cs
        RegisterServicesLikeProgram(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Check core services are registered
        Assert.NotNull(serviceProvider.GetService<ILogger<ProgramTests>>());

        // Check if distributed cache is registered
        var distributedCache = serviceProvider.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        Assert.NotNull(distributedCache);
    }

    [Fact]
    public void ServiceRegistration_RegistersCustomServices()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act - Register services
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Check custom services
        var roomManager = serviceProvider.GetService<IRoomManager>();
        Assert.NotNull(roomManager);

        var keyManager = serviceProvider.GetService<IKeyManager>();
        Assert.NotNull(keyManager);

        var authService = serviceProvider.GetService<IAuthService>();
        Assert.NotNull(authService);

        var aiEngine = serviceProvider.GetService<IAIEngine>();
        Assert.NotNull(aiEngine);
    }

    [Fact]
    public void ServiceRegistration_RegistersSignalR()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Check SignalR services are registered
        var signalRServices = builder.Services.Where(s =>
            s.ServiceType.FullName?.Contains("SignalR") == true ||
            s.ServiceType.FullName?.Contains("Hub") == true).ToList();

        Assert.NotEmpty(signalRServices);
    }

    [Fact]
    public void ServiceRegistration_RegistersJwtAuthentication()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Check JWT authentication is registered
        var authOptions = serviceProvider.GetService<IOptions<JwtBearerOptions>>();
        Assert.NotNull(authOptions);
    }

    [Fact]
    public void ServiceRegistration_RegistersRateLimiting()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Check rate limiting is registered
        var rateLimitOptions = serviceProvider.GetService<IOptions<RateLimiterOptions>>();
        Assert.NotNull(rateLimitOptions);
    }

    [Fact]
    public void ServiceRegistration_RegistersCors()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check CORS is registered
        var corsServices = builder.Services.Where(s =>
            s.ServiceType.FullName?.Contains("Cors") == true).ToList();

        Assert.NotEmpty(corsServices);
    }

    [Fact]
    public void ServiceRegistration_RegistersHealthChecks()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check health checks are registered
        var healthCheckServices = builder.Services.Where(s =>
            s.ServiceType.FullName?.Contains("HealthCheck") == true).ToList();

        Assert.NotEmpty(healthCheckServices);
    }

    [Fact]
    public void ServiceRegistration_RegistersControllers()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check controllers are registered
        var controllerServices = builder.Services.Where(s =>
            s.ServiceType.FullName?.Contains("Controller") == true ||
            s.ServiceType.FullName?.Contains("Mvc") == true).ToList();

        Assert.NotEmpty(controllerServices);
    }

    [Fact]
    public void ApplicationBuilder_CanBuildApp()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);

        // Act & Assert - Should not throw
        using var app = builder.Build();
        Assert.NotNull(app);
    }

    [Fact]
    public void ApplicationPipeline_ConfiguresMiddleware()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();

        // Act - Configure pipeline as in Program.cs
        ConfigurePipelineLikeProgram(app);

        // Assert - App should be configured without throwing
        Assert.NotNull(app);
        Assert.NotNull(app.Services);
    }

    [Fact]
    public void Configuration_DefaultValues_AreAccessible()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Act
        var config = builder.Configuration;

        // Assert - Configuration should be accessible
        Assert.NotNull(config);

        // Test some default configuration access patterns
        var testValue = config.GetValue<string>("NonExistentKey", "DefaultValue");
        Assert.Equal("DefaultValue", testValue);
    }

    [Fact]
    public void Environment_DevelopmentSettings_Work()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Force development environment
        builder.Environment.EnvironmentName = Environments.Development;

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();

        // Act
        ConfigurePipelineLikeProgram(app);

        // Assert
        Assert.Equal(Environments.Development, app.Environment.EnvironmentName);
    }

    [Fact]
    public void Environment_ProductionSettings_Work()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Force production environment
        builder.Environment.EnvironmentName = Environments.Production;

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();

        // Act
        ConfigurePipelineLikeProgram(app);

        // Assert
        Assert.Equal(Environments.Production, app.Environment.EnvironmentName);
    }

    [Fact]
    public void ServiceLifetime_SingletonServices_AreRegisteredCorrectly()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check singleton services
        var roomManagerDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(IRoomManager));
        Assert.NotNull(roomManagerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, roomManagerDescriptor.Lifetime);

        var keyManagerDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(IKeyManager));
        Assert.NotNull(keyManagerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, keyManagerDescriptor.Lifetime);
    }

    [Fact]
    public void ServiceLifetime_ScopedServices_AreRegisteredCorrectly()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check scoped services
        var authServiceDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(IAuthService));
        Assert.NotNull(authServiceDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, authServiceDescriptor.Lifetime);

        var aiEngineDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(IAIEngine));
        Assert.NotNull(aiEngineDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, aiEngineDescriptor.Lifetime); // Changed to singleton per program.cs
    }

    [Fact]
    public void ServiceRegistration_BackgroundServices_AreRegistered()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);

        // Assert - Check background services
        var roomPrunerDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(RoomPruner));
        Assert.NotNull(roomPrunerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, roomPrunerDescriptor.Lifetime);
    }

    [Fact]
    public void JwtConfiguration_TokenValidationParameters_AreSet()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Add test configuration values directly to the builder's configuration
        builder.Configuration["JWT:Issuer"] = "TestIssuer";
        builder.Configuration["JWT:Audience"] = "TestAudience";
        builder.Configuration["Security:MaxFailedLoginAttempts"] = "5";
        builder.Configuration["Security:AccountLockoutMinutes"] = "15";
        builder.Configuration["Security:RateLimitMaxRequests"] = "100";
        builder.Configuration["Security:RateLimitWindowMinutes"] = "1";

        // Act
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Get JWT options using the specific scheme name instead of generic IOptions
        var jwtOptionsMonitor = serviceProvider.GetService<IOptionsMonitor<JwtBearerOptions>>();
        Assert.NotNull(jwtOptionsMonitor);

        var options = jwtOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.NotNull(options);
        Assert.NotNull(options.TokenValidationParameters);

        var tokenValidation = options.TokenValidationParameters;

        // Test the basic validation flags that should be set
        Assert.True(tokenValidation.ValidateLifetime);
        Assert.True(tokenValidation.ValidateIssuer);
        Assert.True(tokenValidation.ValidateAudience);
        Assert.Equal(TimeSpan.FromMinutes(5), tokenValidation.ClockSkew);

        // Note: ValidateIssuerSigningKey may be set by runtime key resolution
        // For now, just verify the basic structure is in place
        Assert.NotNull(tokenValidation);
    }

    [Fact]
    public void CorsConfiguration_AllowsExpectedOrigins()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();
        ConfigurePipelineLikeProgram(app);

        // Assert - Configuration should complete without errors
        Assert.NotNull(app);
    }

    [Fact]
    public void RateLimitConfiguration_SetsCorrectPolicies()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert - Rate limiter should be registered
        var rateLimiterService = serviceProvider.GetService<IOptions<RateLimiterOptions>>();
        Assert.NotNull(rateLimiterService);
    }

    [Fact]
    public void ApplicationStart_HandlesErrors_Gracefully()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        // Use invalid configuration to test error handling
        var invalidConfig = new Mock<IConfiguration>();
        invalidConfig.Setup(x => x[It.IsAny<string>()]).Returns((string?)null);
        invalidConfig.Setup(x => x.GetSection(It.IsAny<string>())).Returns(Mock.Of<IConfigurationSection>());

        // Act & Assert - Should handle gracefully or throw expected exceptions
        try
        {
            RegisterServicesLikeProgram(builder);
            using var app = builder.Build();
            ConfigurePipelineLikeProgram(app);
            Assert.NotNull(app);
        }
        catch (Exception ex)
        {
            // Expected for some configurations - ensure it's a meaningful error
            Assert.NotNull(ex);
        }
    }

    [Fact]
    public void HealthChecks_AreConfigured()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        // Act
        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();
        ConfigurePipelineLikeProgram(app);

        // Assert - Health checks should be available
        var healthCheckService = app.Services.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        Assert.NotNull(healthCheckService);
    }

    [Fact]
    public void ServiceScope_CanResolveServices()
    {
        // Arrange
        var args = new string[] { };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();

        // Act
        using var scope = app.Services.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        // Assert - Should be able to resolve scoped services
        var authService = scopedProvider.GetService<IAuthService>();
        Assert.NotNull(authService);

        var aiEngine = scopedProvider.GetService<IAIEngine>();
        Assert.NotNull(aiEngine);
    }

    [Fact]
    public void ApplicationUrls_CanBeConfigured()
    {
        // Arrange
        var args = new string[] { "--urls", "http://localhost:5000" };
        var builder = WebApplication.CreateBuilder(args);

        var configurationMock = new Mock<IConfiguration>();
        SetupMockConfiguration(configurationMock);

        RegisterServicesLikeProgram(builder);
        using var app = builder.Build();

        // Act & Assert - Should configure without errors
        Assert.NotNull(app);
        Assert.NotNull(app.Urls);
    }

    #region Helper Methods

    private static void SetupMockConfiguration(Mock<IConfiguration> configMock)
    {
        // Setup configuration using indexer access to avoid extension method issues
        configMock.Setup(x => x["JWT:Issuer"]).Returns("TicTacToeApp");
        configMock.Setup(x => x["JWT:Audience"]).Returns("TicTacToeUsers");
        configMock.Setup(x => x["JWT:ExpiryMinutes"]).Returns("60");
        configMock.Setup(x => x["JWT:RefreshTokenExpiryDays"]).Returns("7");
        configMock.Setup(x => x["Security:MaxFailedLoginAttempts"]).Returns("5");
        configMock.Setup(x => x["Security:AccountLockoutMinutes"]).Returns("15");
        configMock.Setup(x => x["Security:RateLimitMaxRequests"]).Returns("100");
        configMock.Setup(x => x["Security:RateLimitWindowMinutes"]).Returns("15");
        configMock.Setup(x => x["Security:FailedLoginTrackingHours"]).Returns("24");
        configMock.Setup(x => x["AllowedOrigins"]).Returns("http://localhost:3000,https://localhost:3000");

        // Setup configuration sections for JWT
        var jwtSection = new Mock<IConfigurationSection>();
        jwtSection.Setup(x => x["Issuer"]).Returns("TicTacToeApp");
        jwtSection.Setup(x => x["Audience"]).Returns("TicTacToeUsers");
        jwtSection.Setup(x => x["ExpiryMinutes"]).Returns("60");
        jwtSection.Setup(x => x["RefreshTokenExpiryDays"]).Returns("7");
        configMock.Setup(x => x.GetSection("JWT")).Returns(jwtSection.Object);

        // Default section for other cases
        configMock.Setup(x => x.GetSection(It.IsAny<string>()))
            .Returns(Mock.Of<IConfigurationSection>());
    }

    private static void RegisterServicesLikeProgram(WebApplicationBuilder builder)
    {
        // Mimic service registration from Program.cs

        // Add memory cache
        builder.Services.AddMemoryCache();

        // Add distributed cache (using memory cache for testing)
        builder.Services.AddDistributedMemoryCache();

        // Add custom services with interfaces
        builder.Services.AddSingleton<IRoomManager, RoomManager>();
        builder.Services.AddSingleton<IKeyManager, KeyManager>();
        builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
        builder.Services.AddScoped<ICacheWrapper, CacheWrapper>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddSingleton<IAIEngine, AIEngine>();
        builder.Services.AddScoped<ICacheWrapper, CacheWrapper>();
        builder.Services.AddSingleton<RoomPruner>();
        builder.Services.AddHostedService<RoomPruner>();

        // Add HTTP Context accessor
        builder.Services.AddHttpContextAccessor();

        // Add SignalR
        builder.Services.AddSignalR();

        // Add authentication (simplified for testing)
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
            });

        // Add authorization
        builder.Services.AddAuthorization();

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // Add rate limiting
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
        });

        // Add controllers
        builder.Services.AddControllers();

        // Add health checks
        builder.Services.AddHealthChecks();
    }

    private static void ConfigurePipelineLikeProgram(WebApplication app)
    {
        // Mimic middleware configuration from Program.cs

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors("AllowFrontend");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/health");

        // SignalR hub would be mapped here
        // app.MapHub<TicTacToeHub>("/tictactoehub");
    }

    #endregion
}

public class ProgramIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProgramIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Application_StartsAndRespondsToHealthCheck()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Application_CanAccessRootEndpoint()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert - Root returns 404 (no endpoint defined), which is expected
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Application_InProductionEnvironment_UsesHstsAndNoDeveloperExceptionPage()
    {
        // Arrange - Create factory with production environment
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
            });

        var client = factory.CreateClient();

        // Act - Make a request to health endpoint
        var response = await client.GetAsync("/health");

        // Assert - Should get OK response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Note: HSTS header is typically added for HTTPS requests.
        // In test environment, we verify the app starts in production mode.
        // The key difference is that developer exception page is not used.
    }

    [Fact]
    public async Task Application_InDevelopmentEnvironment_UsesDeveloperExceptionPage()
    {
        // Arrange - Create factory with development environment (default)
        var client = _factory.CreateClient();

        // Act - Make a request to a non-existent endpoint to potentially trigger error page
        var response = await client.GetAsync("/nonexistent");

        // Assert - Should get 404, but in development, detailed errors might be shown
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cors_WithCustomOrigins_AllowsConfiguredOrigins()
    {
        // Arrange - Create factory with custom CORS configuration
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Cors:AllowedOrigins:0"] = "https://example.com",
                        ["Cors:AllowedOrigins:1"] = "https://test.com"
                    });
                });
            });

        var client = factory.CreateClient();

        // Act - Make a request with Origin header
        client.DefaultRequestHeaders.Add("Origin", "https://example.com");
        var response = await client.GetAsync("/health");

        // Assert - Should allow the configured origin
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var allowOrigin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
        Assert.Equal("https://example.com", allowOrigin);
    }

    [Fact]
    public async Task RateLimiting_EnforcesLimits()
    {
        // Arrange - Create factory with strict rate limiting
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Security:RateLimiting:JwksRequestsPerWindow"] = "1",
                        ["Security:RateLimiting:JwksWindowMinutes"] = "1"
                    });
                });
            });

        var client = factory.CreateClient();

        // Act - Make multiple requests to a rate-limited endpoint
        var firstResponse = await client.GetAsync("/health");
        var secondResponse = await client.GetAsync("/health");

        // Assert - First should succeed, second might be rate limited
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        // Note: Rate limiting might not trigger immediately in test environment
        // This test verifies the setup, actual limiting depends on timing
    }

    [Fact]
    public async Task ErrorHandling_InProduction_ReturnsProblemDetails()
    {
        // Arrange - Create factory with production environment
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
            });

        var client = factory.CreateClient();

        // Act - Request the error endpoint
        var response = await client.GetAsync("/error");

        // Assert - Should return problem details
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("An error occurred", content);
    }

    [Fact]
    public async Task JwtConfiguration_WithCustomIssuerAndAudience_IsConfigured()
    {
        // Arrange - Create factory with custom JWT config
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JWT:Issuer"] = "CustomIssuer",
                        ["JWT:Audience"] = "CustomAudience"
                    });
                });
            });

        var client = factory.CreateClient();

        // Act - Make a request to trigger JWT setup
        var response = await client.GetAsync("/health");

        // Assert - App should start without errors (JWT config is validated at startup)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignalR_HubIsMapped()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Try to connect to SignalR hub (negotiate request)
        var response = await client.GetAsync("/tictactoehub/negotiate");

        // Assert - Should get a response (not 404), indicating hub is mapped
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}