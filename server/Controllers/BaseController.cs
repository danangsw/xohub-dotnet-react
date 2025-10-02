using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace XoHub.Server.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected readonly IConfiguration _configuration;

    public ApiControllerBase(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the API version prefix from configuration (e.g., "api/v1").
    /// This is used for consistent API versioning across controllers.
    /// </summary>
    protected string ApiVersionPrefix
    {
        get
        {
            const string DEFAULT_PREFIX = "api/v1";
            return _configuration.GetValue<string>("ApiSettings:VersionPrefix", DEFAULT_PREFIX);
        }
    }
}