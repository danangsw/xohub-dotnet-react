using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace XoHub.Server.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private readonly string API_VERSION_PREFIX = "api/v1";
    protected readonly IConfiguration _configuration;
    protected string ApiVersionPrefix => _configuration.GetValue<string>("ApiSettings:VersionPrefix", API_VERSION_PREFIX);

    public ApiControllerBase(IConfiguration configuration)
    {
        _configuration = configuration;
    }
}