using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace XoHub.Server.Services;

/// <summary>
/// Wrapper interface for distributed cache operations to enable proper unit testing
/// </summary>
public interface ICacheWrapper
{
    Task<string?> GetStringAsync(string key, CancellationToken token = default);
    Task SetStringAsync(string key, string value, CancellationToken token = default);
    Task SetStringAsync(string key, string value, DistributedCacheEntryOptions options, CancellationToken token = default);
    Task RemoveAsync(string key, CancellationToken token = default);
}

/// <summary>
/// Implementation of ICacheWrapper using IDistributedCache
/// </summary>
public class CacheWrapper : ICacheWrapper
{
    private readonly IDistributedCache _cache;

    public CacheWrapper(IDistributedCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public Task<string?> GetStringAsync(string key, CancellationToken token = default)
    {
        return _cache.GetStringAsync(key, token);
    }

    public Task SetStringAsync(string key, string value, CancellationToken token = default)
    {
        return _cache.SetStringAsync(key, value, token);
    }

    public Task SetStringAsync(string key, string value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        return _cache.SetStringAsync(key, value, options, token);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        return _cache.RemoveAsync(key, token);
    }
}