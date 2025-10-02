using Microsoft.Extensions.Configuration;

namespace XoHub.Server.Services;

public interface IConfigurationService
{
    int GetMaxFailedLoginAttempts();
    int GetAccountLockoutMinutes();
    int GetRateLimitMaxRequests();
    int GetRateLimitWindowMinutes();
    int GetFailedLoginTrackingHours();
}

public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _configuration;

    public ConfigurationService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public int GetMaxFailedLoginAttempts()
    {
        return _configuration.GetValue<int>("Security:MaxFailedLoginAttempts", 5);
    }

    public int GetAccountLockoutMinutes()
    {
        return _configuration.GetValue<int>("Security:AccountLockoutMinutes", 15);
    }

    public int GetRateLimitMaxRequests()
    {
        return _configuration.GetValue<int>("Security:RateLimitMaxRequests", 10);
    }

    public int GetRateLimitWindowMinutes()
    {
        return _configuration.GetValue<int>("Security:RateLimitWindowMinutes", 15);
    }

    public int GetFailedLoginTrackingHours()
    {
        return _configuration.GetValue<int>("Security:FailedLoginTrackingHours", 24);
    }
}