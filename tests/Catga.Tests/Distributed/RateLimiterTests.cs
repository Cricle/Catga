using Catga.Abstractions;
using Catga.Persistence.Redis.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Distributed;

[Trait("Requires", "Docker")]
public class RateLimiterTests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        if (!IsDockerAvailable())
        {
            return;
        }

        _container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
            await _redis.CloseAsync();
        if (_container != null)
            await _container.DisposeAsync();
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task TryAcquire_WithinLimit_Succeeds()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 10,
            DefaultWindow = TimeSpan.FromMinutes(1),
            Algorithm = RateLimitAlgorithm.FixedWindow
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);

        var result = await limiter.TryAcquireAsync("test-key", 1);

        result.IsAcquired.Should().BeTrue();
        result.RemainingPermits.Should().Be(9);
    }

    [Fact]
    public async Task TryAcquire_ExceedsLimit_Rejected()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 3,
            DefaultWindow = TimeSpan.FromMinutes(1),
            Algorithm = RateLimitAlgorithm.FixedWindow
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);
        var key = "limit-test-" + Guid.NewGuid().ToString("N");

        // Acquire all permits
        for (int i = 0; i < 3; i++)
        {
            var r = await limiter.TryAcquireAsync(key, 1);
            r.IsAcquired.Should().BeTrue();
        }

        // Next should be rejected
        var result = await limiter.TryAcquireAsync(key, 1);

        result.IsAcquired.Should().BeFalse();
        result.Reason.Should().Be(RateLimitRejectionReason.RateLimitExceeded);
        result.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_SlidingWindow_Works()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 5,
            DefaultWindow = TimeSpan.FromSeconds(2),
            Algorithm = RateLimitAlgorithm.SlidingWindow,
            SlidingWindowSegments = 4
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);
        var key = "sliding-test-" + Guid.NewGuid().ToString("N");

        // Acquire all permits
        for (int i = 0; i < 5; i++)
        {
            var r = await limiter.TryAcquireAsync(key, 1);
            r.IsAcquired.Should().BeTrue();
        }

        // Should be rejected
        var rejected = await limiter.TryAcquireAsync(key, 1);
        rejected.IsAcquired.Should().BeFalse();

        // Wait for window to slide
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        // Should be allowed again
        var allowed = await limiter.TryAcquireAsync(key, 1);
        allowed.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectData()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 10,
            DefaultWindow = TimeSpan.FromMinutes(1),
            Algorithm = RateLimitAlgorithm.FixedWindow
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);
        var key = "stats-test-" + Guid.NewGuid().ToString("N");

        // Acquire some permits
        await limiter.TryAcquireAsync(key, 3);

        var stats = await limiter.GetStatisticsAsync(key);

        stats.Should().NotBeNull();
        stats!.Value.CurrentCount.Should().Be(3);
        stats.Value.Limit.Should().Be(10);
    }

    [Fact]
    public async Task WaitAsync_AcquiresWhenAvailable()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 2,
            DefaultWindow = TimeSpan.FromSeconds(1),
            Algorithm = RateLimitAlgorithm.FixedWindow
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);
        var key = "wait-test-" + Guid.NewGuid().ToString("N");

        // Exhaust permits
        await limiter.TryAcquireAsync(key, 2);

        // Wait should succeed after window resets
        var result = await limiter.WaitAsync(key, 1, TimeSpan.FromSeconds(3));

        result.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task WaitAsync_TimesOut()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 1,
            DefaultWindow = TimeSpan.FromMinutes(1),
            Algorithm = RateLimitAlgorithm.FixedWindow
        });

        var limiter = new RedisRateLimiter(_redis, options, NullLogger<RedisRateLimiter>.Instance);
        var key = "timeout-test-" + Guid.NewGuid().ToString("N");

        // Exhaust permits
        await limiter.TryAcquireAsync(key, 1);

        // Wait should timeout
        var result = await limiter.WaitAsync(key, 1, TimeSpan.FromMilliseconds(500));

        result.IsAcquired.Should().BeFalse();
        result.Reason.Should().Be(RateLimitRejectionReason.Timeout);
    }
}
