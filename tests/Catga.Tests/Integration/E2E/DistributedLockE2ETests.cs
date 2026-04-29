using FluentAssertions;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Medallion.Threading.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;
using System.Diagnostics;

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

        var provider = new RedisDistributedSynchronizationProvider(_redis.GetDatabase());
        var distributedLock = provider.CreateLock($"test-resource-{Guid.NewGuid():N}");

        await using var handle1 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle1.Should().NotBeNull();

        var handle2 = await distributedLock.TryAcquireAsync(TimeSpan.FromMilliseconds(100));
        handle2.Should().BeNull();

        await handle1!.DisposeAsync();

        await using var handle3 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle3.Should().NotBeNull();
    }

    [Fact]
    public async Task Redis_DistributedLock_AcquireWithWait()
    {
        if (_redis is null) return;

        var provider = new RedisDistributedSynchronizationProvider(_redis.GetDatabase());
        var distributedLock = provider.CreateLock($"wait-resource-{Guid.NewGuid():N}");

        await using var handle1 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle1.Should().NotBeNull();

        var waiter = Task.Run(() => distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(2)).AsTask());

        await Task.Delay(200);
        await handle1!.DisposeAsync();

        await using var handle2 = await waiter;
        handle2.Should().NotBeNull();
    }

    [Fact]
    public async Task Redis_DistributedLock_TryAcquireAsync_TimesOutWhileHeld()
    {
        if (_redis is null) return;

        var provider = new RedisDistributedSynchronizationProvider(_redis.GetDatabase());
        var distributedLock = provider.CreateLock($"timeout-resource-{Guid.NewGuid():N}");

        await using var handle1 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle1.Should().NotBeNull();

        var sw = Stopwatch.StartNew();
        var handle2 = await distributedLock.TryAcquireAsync(TimeSpan.FromMilliseconds(250));
        sw.Stop();

        handle2.Should().BeNull();
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Redis_DistributedLock_ConcurrentAccess_IsStrictlyMutuallyExclusive()
    {
        if (_redis is null) return;

        var provider = new RedisDistributedSynchronizationProvider(_redis.GetDatabase());
        await AssertStrictMutualExclusionAsync(resource => provider.CreateLock(resource), $"redis-concurrent-{Guid.NewGuid():N}", 10, 10);
    }

    [Fact]
    public async Task InMemory_DistributedLock_AcquireAndRelease()
    {
        var provider = CreateInMemoryProvider();
        var distributedLock = provider.CreateLock($"mem-resource-{Guid.NewGuid():N}");

        await using var handle1 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle1.Should().NotBeNull();

        var handle2 = await distributedLock.TryAcquireAsync(TimeSpan.FromMilliseconds(100));
        handle2.Should().BeNull();

        await handle1!.DisposeAsync();

        await using var handle3 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle3.Should().NotBeNull();
    }

    [Fact]
    public async Task InMemory_DistributedLock_ConcurrentAccess_IsStrictlyMutuallyExclusive()
    {
        var provider = CreateInMemoryProvider();
        await AssertStrictMutualExclusionAsync(resource => provider.CreateLock(resource), $"mem-concurrent-{Guid.NewGuid():N}", 20, 5);
    }

    [Fact]
    public async Task InMemory_DistributedLock_TryAcquireAsync_TimesOutWhileHeld()
    {
        var provider = CreateInMemoryProvider();
        var distributedLock = provider.CreateLock($"mem-timeout-resource-{Guid.NewGuid():N}");

        await using var handle1 = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(1));
        handle1.Should().NotBeNull();

        var sw = Stopwatch.StartNew();
        var handle2 = await distributedLock.TryAcquireAsync(TimeSpan.FromMilliseconds(250));
        sw.Stop();

        handle2.Should().BeNull();
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    private static FileDistributedSynchronizationProvider CreateInMemoryProvider()
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "catga-lock-tests", Guid.NewGuid().ToString("N")));
        directory.Create();
        return new FileDistributedSynchronizationProvider(directory);
    }

    private static async Task AssertStrictMutualExclusionAsync(
        Func<string, IDistributedLock> lockFactory,
        string resource,
        int taskCount,
        int criticalSectionDelayMs)
    {
        var counter = 0;
        var active = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, taskCount).Select(async _ =>
        {
            var distributedLock = lockFactory(resource);
            await using var handle = await distributedLock.TryAcquireAsync(TimeSpan.FromSeconds(5));
            handle.Should().NotBeNull();

            var currentConcurrent = Interlocked.Increment(ref active);
            UpdateMax(ref maxConcurrent, currentConcurrent);
            try
            {
                var current = counter;
                await Task.Delay(criticalSectionDelayMs);
                counter = current + 1;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        await Task.WhenAll(tasks);

        counter.Should().Be(taskCount);
        maxConcurrent.Should().Be(1, "critical section should never have more than one concurrent holder");
    }

    private static void UpdateMax(ref int maxConcurrent, int currentConcurrent)
    {
        while (true)
        {
            var snapshot = maxConcurrent;
            if (currentConcurrent <= snapshot) return;
            if (Interlocked.CompareExchange(ref maxConcurrent, currentConcurrent, snapshot) == snapshot) return;
        }
    }
}
