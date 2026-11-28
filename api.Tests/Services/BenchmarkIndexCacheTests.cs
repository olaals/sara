using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using api.Configurations;
using api.Services.BenchmarkServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Test.Services;

public class BenchmarkIndexCacheTests
{
    private static BenchmarkIndexCache CreateCache(
        long sizeLimitBytes = BenchmarkOptions.DefaultCacheSizeLimitBytes,
        int ttlMinutes = BenchmarkOptions.DefaultCacheEntryTtlMinutes
    ) =>
        new(
            Options.Create(
                new BenchmarkOptions
                {
                    CacheSizeLimitBytes = sizeLimitBytes,
                    CacheEntryTtlMinutes = ttlMinutes,
                }
            ),
            NullLogger<BenchmarkIndexCache>.Instance
        );

    [Fact]
    public async Task ConcurrentMissesForSameKeyInvokeLoaderOnce()
    {
        using var cache = CreateCache();
        var loaderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseLoader = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var calls = 0;

        async Task<CachedBenchmarkPayload<object>> Load(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            loaderStarted.TrySetResult();
            await releaseLoader.Task;
            return new CachedBenchmarkPayload<object>(new object(), 512);
        }

        var tasks = Enumerable
            .Range(0, 16)
            .Select(_ =>
                cache.GetOrLoadAsync("shared-key", Load, TestContext.Current.CancellationToken)
            )
            .ToArray();

        await loaderStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        releaseLoader.SetResult();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(results, item => Assert.Same(results[0], item));
    }

    [Fact]
    public async Task EntryExceedingSizeLimitIsReturnedWithoutBeingCached()
    {
        using var cache = CreateCache(sizeLimitBytes: 100);
        var calls = 0;

        Task<CachedBenchmarkPayload<string>> Load(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new CachedBenchmarkPayload<string>("too-large", 101));
        }

        var first = await cache.GetOrLoadAsync("huge", Load, TestContext.Current.CancellationToken);
        var second = await cache.GetOrLoadAsync(
            "huge",
            Load,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("too-large", first);
        Assert.Equal("too-large", second);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CacheDoesNotHoldEntriesWhoseCombinedSizeExceedsSizeLimit()
    {
        using var cache = CreateCache(sizeLimitBytes: 100);

        await cache.GetOrLoadAsync(
            "key-a",
            _ => Task.FromResult(new CachedBenchmarkPayload<string>("a", 60)),
            TestContext.Current.CancellationToken
        );
        await cache.GetOrLoadAsync(
            "key-b",
            _ => Task.FromResult(new CachedBenchmarkPayload<string>("b", 60)),
            TestContext.Current.CancellationToken
        );

        var reloads = 0;
        await cache.GetOrLoadAsync(
            "key-a",
            _ =>
            {
                reloads++;
                return Task.FromResult(new CachedBenchmarkPayload<string>("a", 60));
            },
            TestContext.Current.CancellationToken
        );
        await cache.GetOrLoadAsync(
            "key-b",
            _ =>
            {
                reloads++;
                return Task.FromResult(new CachedBenchmarkPayload<string>("b", 60));
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(reloads >= 1);
    }

    [Fact]
    public async Task LoaderExceptionIsNotCachedAndSubsequentCallRetries()
    {
        using var cache = CreateCache();
        var attempt = 0;

        Task<CachedBenchmarkPayload<string>> Load(CancellationToken _)
        {
            attempt++;
            if (attempt == 1)
            {
                throw new InvalidOperationException("transient blob error");
            }

            return Task.FromResult(new CachedBenchmarkPayload<string>("recovered", 64));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrLoadAsync("flaky", Load, TestContext.Current.CancellationToken)
        );

        var recovered = await cache.GetOrLoadAsync(
            "flaky",
            Load,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("recovered", recovered);
        Assert.Equal(2, attempt);
    }
}
