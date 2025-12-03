using Catga.Abstractions;
using Catga.Persistence.InMemory.Locking;
using Catga.Persistence.Redis.Locking;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class DistributedLockE2ETests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
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
    public async Task Redis_DistributedLock_AcquireAndRelease()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedLockOptions());
        var logger = NullLogger<RedisDistributedLock>.Instance;
        var lockService = new RedisDistributedLock(_redis, options, logger);

        // Acquire lock
        await using var handle = await lockService.TryAcquireAsync("test-resource", TimeSpan.FromSeconds(30));
        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("test-resource");
        handle.IsValid.Should().BeTrue();

        // Verify locked
        var isLocked = await lockService.IsLockedAsync("test-resource");
        isLocked.Should().BeTrue();

        // Try acquire same resource - should fail
        var handle2 = await lockService.TryAcquireAsync("test-resource", TimeSpan.FromSeconds(30));
        handle2.Should().BeNull();

        // Release and verify
        await handle.DisposeAsync();
        await Task.Delay(100);

        var isLockedAfter = await lockService.IsLockedAsync("test-resource");
        isLockedAfter.Should().BeFalse();
    }

    [Fact]
    public async Task Redis_DistributedLock_AcquireWithWait()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(50) });
        var logger = NullLogger<RedisDistributedLock>.Instance;
        var lockService = new RedisDistributedLock(_redis, options, logger);

        // Acquire first lock
        await using var handle1 = await lockService.TryAcquireAsync("wait-resource", TimeSpan.FromMilliseconds(500));
        handle1.Should().NotBeNull();

        // Start waiting for lock in background
        var acquireTask = Task.Run(async () =>
        {
            return await lockService.AcquireAsync("wait-resource", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        });

        // Release first lock after short delay
        await Task.Delay(200);
        await handle1!.DisposeAsync();

        // Second acquire should succeed
        await using var handle2 = await acquireTask;
        handle2.Should().NotBeNull();
        handle2.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Redis_DistributedLock_ExtendLock()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedLockOptions());
        var logger = NullLogger<RedisDistributedLock>.Instance;
        var lockService = new RedisDistributedLock(_redis, options, logger);

        await using var handle = await lockService.TryAcquireAsync("extend-resource", TimeSpan.FromSeconds(2));
        handle.Should().NotBeNull();

        var originalExpiry = handle!.ExpiresAt;

        // Extend lock
        await handle.ExtendAsync(TimeSpan.FromSeconds(10));

        handle.ExpiresAt.Should().BeAfter(originalExpiry);
        handle.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Redis_DistributedLock_ConcurrentAccess()
    {
        if (_redis is null) return;

        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<RedisDistributedLock>.Instance;
        var lockService = new RedisDistributedLock(_redis, options, logger);

        var counter = 0;
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var handle = await lockService.AcquireAsync(
                "concurrent-resource",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10));

            // Critical section
            var current = counter;
            await Task.Delay(10);
            counter = current + 1;
        });

        await Task.WhenAll(tasks);

        // Without proper locking, counter would be less than 10 due to race conditions
        counter.Should().Be(10);
    }

    [Fact]
    public async Task InMemory_DistributedLock_AcquireAndRelease()
    {
        var options = Options.Create(new DistributedLockOptions());
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Acquire lock
        await using var handle = await lockService.TryAcquireAsync("mem-resource", TimeSpan.FromSeconds(30));
        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("mem-resource");
        handle.IsValid.Should().BeTrue();

        // Verify locked
        var isLocked = await lockService.IsLockedAsync("mem-resource");
        isLocked.Should().BeTrue();

        // Try acquire same resource - should fail
        var handle2 = await lockService.TryAcquireAsync("mem-resource", TimeSpan.FromSeconds(30));
        handle2.Should().BeNull();

        // Release and verify
        await handle.DisposeAsync();

        var isLockedAfter = await lockService.IsLockedAsync("mem-resource");
        isLockedAfter.Should().BeFalse();
    }

    [Fact]
    public async Task InMemory_DistributedLock_ExpiryWorks()
    {
        var options = Options.Create(new DistributedLockOptions());
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Acquire lock with short expiry
        var handle = await lockService.TryAcquireAsync("expiry-resource", TimeSpan.FromMilliseconds(200));
        handle.Should().NotBeNull();

        // Wait for expiry
        await Task.Delay(300);

        // Should be able to acquire again
        await using var handle2 = await lockService.TryAcquireAsync("expiry-resource", TimeSpan.FromSeconds(30));
        handle2.Should().NotBeNull();
    }

    [Fact]
    public async Task InMemory_DistributedLock_ConcurrentAccess()
    {
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(5) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        var counter = 0;
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await using var handle = await lockService.AcquireAsync(
                "mem-concurrent",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10));

            // Critical section
            var current = counter;
            await Task.Delay(5);
            counter = current + 1;
        });

        await Task.WhenAll(tasks);

        counter.Should().Be(20);
    }
}
