using System.Threading;
using System.Threading.Tasks;

namespace XoHub.Server.Services;

/// <summary>
/// Wrapper interface for distributed cache operations to enable proper unit testing
/// </summary>
public interface ICacheWrapper
{
    Task<string?> GetStringAsync(string key, CancellationToken token);
    Task SetStringAsync(string key, string value, CancellationToken token);
}