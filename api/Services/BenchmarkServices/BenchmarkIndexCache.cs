using api.Configurations;
using api.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace api.Services.BenchmarkServices;

public readonly record struct CachedBenchmarkPayload<T>(T? Value, long EstimatedBytes);

public interface IBenchmarkIndexCache
{
    Task<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<CachedBenchmarkPayload<T>>> loader,
        CancellationToken cancellationToken = default
    )
        where T : class;
}

public sealed class BenchmarkIndexCache : IBenchmarkIndexCache, IDisposable
{
    private const double CacheCompactionPercentage = 0.25;

    private readonly MemoryCache _cache;
    private readonly long _sizeLimitBytes;
    private readonly TimeSpan _entryTtl;
    private readonly ILogger<BenchmarkIndexCache> _logger;
    private readonly object _keyGatesLock = new();
    private readonly Dictionary<string, KeyGate> _keyGates = new(StringComparer.Ordinal);

    public BenchmarkIndexCache(
        IOptions<BenchmarkOptions> options,
        ILogger<BenchmarkIndexCache> logger
    )
    {
        var config = options.Value;
        _sizeLimitBytes =
            config.CacheSizeLimitBytes > 0
                ? config.CacheSizeLimitBytes
                : BenchmarkOptions.DefaultCacheSizeLimitBytes;
        _entryTtl = TimeSpan.FromMinutes(
            config.CacheEntryTtlMinutes > 0
                ? config.CacheEntryTtlMinutes
                : BenchmarkOptions.DefaultCacheEntryTtlMinutes
        );
        _logger = logger;
        _cache = new MemoryCache(
            new MemoryCacheOptions
            {
                SizeLimit = _sizeLimitBytes,
                CompactionPercentage = CacheCompactionPercentage,
            }
        );
    }

    public async Task<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<CachedBenchmarkPayload<T>>> loader,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        if (_cache.TryGetValue(key, out T? cached))
        {
            return cached;
        }

        var keyGate = AcquireKeyGate(key);
        var enteredGate = false;
        try
        {
            await keyGate.Semaphore.WaitAsync(cancellationToken);
            enteredGate = true;

            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var payload = await loader(cancellationToken);
            if (payload.Value is null)
            {
                return null;
            }

            StoreIfWithinBudget(key, payload.Value, payload.EstimatedBytes);
            return payload.Value;
        }
        finally
        {
            if (enteredGate)
            {
                keyGate.Semaphore.Release();
            }

            ReleaseKeyGate(key, keyGate);
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private void StoreIfWithinBudget<T>(string key, T value, long estimatedBytes)
        where T : class
    {
        var entrySize = Math.Max(1L, estimatedBytes);
        if (entrySize > _sizeLimitBytes)
        {
            _logger.LogWarning(
                "Skipping benchmark cache entry '{CacheKey}' ({EstimatedBytes} bytes) because it exceeds the cache size limit ({SizeLimitBytes} bytes).",
                Sanitize.SanitizeUserInput(key),
                entrySize,
                _sizeLimitBytes
            );
            return;
        }

        _cache.Set(
            key,
            value,
            new MemoryCacheEntryOptions
            {
                Size = entrySize,
                AbsoluteExpirationRelativeToNow = _entryTtl,
            }
        );
    }

    private KeyGate AcquireKeyGate(string key)
    {
        lock (_keyGatesLock)
        {
            if (!_keyGates.TryGetValue(key, out var gate))
            {
                gate = new KeyGate();
                _keyGates[key] = gate;
            }

            gate.RefCount++;
            return gate;
        }
    }

    private void ReleaseKeyGate(string key, KeyGate gate)
    {
        var shouldDispose = false;
        lock (_keyGatesLock)
        {
            gate.RefCount--;
            if (gate.RefCount == 0)
            {
                _keyGates.Remove(key);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            gate.Semaphore.Dispose();
        }
    }

    private sealed class KeyGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }
}
