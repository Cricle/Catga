using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration;

/// <summary>
/// 共享的Integration测试基础设施
/// 所有Integration测试共享同一个Redis和NATS容器，大幅提升测试速度
/// </summary>
[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public class IntegrationTestsCollection : ICollectionFixture<SharedIntegrationFixture>
{
}

/// <summary>
/// 共享的Integration测试Fixture
/// 管理Redis和NATS容器的生命周期，在所有测试间共享
/// </summary>
public class SharedIntegrationFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static SharedIntegrationFixture? _instance;
    private static bool _isInitialized = false;

    private RedisContainer? _redisContainer;
    private IContainer? _natsContainer;

    public string? RedisConnectionString { get; private set; }
    public string? NatsConnectionString { get; private set; }
    public IConnectionMultiplexer? Redis { get; private set; }
    public NatsConnection? NatsConnection { get; private set; }
    public INatsJSContext? JetStreamContext { get; private set; }
    public bool IsDockerAvailable { get; private set; }

    public static SharedIntegrationFixture Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SharedIntegrationFixture();
            }
            return _instance;
        }
    }

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            Console.WriteLine("🚀 Initializing shared integration test infrastructure...");

            // 检查 Docker 是否可用
            IsDockerAvailable = await CheckDockerAvailableAsync();
            if (!IsDockerAvailable)
            {
                Console.WriteLine("⚠ Docker not available, integration tests will be skipped");
                _isInitialized = true;
                return;
            }

            // 启动 Redis 容器
            await InitializeRedisAsync();

            // 启动 NATS 容器
            await InitializeNatsAsync();

            _isInitialized = true;
            Console.WriteLine("✓ Shared integration test infrastructure ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to initialize integration infrastructure: {ex.Message}");
            IsDockerAvailable = false;
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeRedisAsync()
    {
        try
        {
            var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
            _redisContainer = new RedisBuilder()
                .WithImage(redisImage)
                .WithName($"catga-integration-redis-{Guid.NewGuid():N}")
                .WithCommand("redis-server", "--save", "", "--appendonly", "no")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                .WithCleanUp(true)
                .Build();

            await _redisContainer.StartAsync();
            RedisConnectionString = _redisContainer.GetConnectionString();

            var options = ConfigurationOptions.Parse(RedisConnectionString);
            options.AllowAdmin = true;
            Redis = await ConnectionMultiplexer.ConnectAsync(options);

            Console.WriteLine($"✓ Redis container started: {RedisConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Redis container failed: {ex.Message}");
        }
    }

    private async Task InitializeNatsAsync()
    {
        try
        {
            var natsImage = Environment.GetEnvironmentVariable("TEST_NATS_IMAGE") ?? "nats:2.10-alpine";
            _natsContainer = new ContainerBuilder()
                .WithImage(natsImage)
                .WithName($"catga-integration-nats-{Guid.NewGuid():N}")
                .WithCommand("-js", "-m", "8222")
                .WithPortBinding(4222, true)
                .WithPortBinding(8222, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPort(8222)
                        .ForPath("/varz")))
                .WithCleanUp(true)
                .Build();

            await _natsContainer.StartAsync();

            var host = _natsContainer.Hostname;
            var port = _natsContainer.GetMappedPublicPort(4222);
            NatsConnectionString = $"nats://{host}:{port}";

            await ConnectToNatsWithRetryAsync(NatsConnectionString);

            Console.WriteLine($"✓ NATS container started: {NatsConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ NATS container failed: {ex.Message}");
        }
    }

    private async Task ConnectToNatsWithRetryAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var connection = new NatsConnection(new NatsOpts
                {
                    Url = connectionString,
                    ConnectTimeout = TimeSpan.FromSeconds(2)
                });

                await connection.ConnectAsync();

                NatsConnection = connection;
                JetStreamContext = new NatsJSContext(connection);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (NatsConnection != null)
                {
                    try { await NatsConnection.DisposeAsync(); } catch { }
                    NatsConnection = null;
                }

                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException("Timed out waiting for NATS connection to become ready.", lastError);
    }

    public async Task DisposeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (!_isInitialized)
                return;

            Console.WriteLine("🛑 Stopping shared integration test infrastructure...");

            if (NatsConnection != null)
            {
                await NatsConnection.DisposeAsync();
            }

            if (Redis != null)
            {
                await Redis.DisposeAsync();
            }

            if (_redisContainer != null)
            {
                await _redisContainer.StopAsync();
                await _redisContainer.DisposeAsync();
                Console.WriteLine("✓ Redis container stopped");
            }

            if (_natsContainer != null)
            {
                await _natsContainer.StopAsync();
                await _natsContainer.DisposeAsync();
                Console.WriteLine("✓ NATS container stopped");
            }

            _isInitialized = false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<bool> CheckDockerAvailableAsync()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 清理 Redis 数据库（测试隔离）
    /// 优化：不使用 FLUSHDB，改用键前缀隔离
    /// </summary>
    public async Task FlushRedisAsync()
    {
        // 不再使用 FLUSHDB，改用键前缀隔离提升性能
        await Task.CompletedTask;
    }

    /// <summary>
    /// 清理 NATS JetStream（测试隔离）
    /// 优化：不删除所有 streams，改用唯一 stream 名称隔离
    /// </summary>
    public async Task CleanupNatsStreamsAsync()
    {
        // 不再删除所有 streams，改用唯一名称隔离提升性能
        await Task.CompletedTask;
    }

    /// <summary>
    /// 生成唯一的键前缀用于测试隔离
    /// </summary>
    public static string GenerateKeyPrefix(string testName)
    {
        return $"test:{testName}:{Guid.NewGuid():N}:";
    }
}

public static class AsyncTestWait
{
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(3);
        var waitPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(25);
        var deadline = DateTime.UtcNow + waitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(waitPollInterval);
        }

        if (await condition())
        {
            return;
        }

        throw new TimeoutException($"Condition was not satisfied within {waitTimeout}.");
    }

    public static async Task<T> WaitUntilAsync<T>(
        Func<Task<T>> valueFactory,
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(3);
        var waitPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(25);
        var deadline = DateTime.UtcNow + waitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var value = await valueFactory();
            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(waitPollInterval);
        }

        var finalValue = await valueFactory();
        if (predicate(finalValue))
        {
            return finalValue;
        }

        throw new TimeoutException($"Condition was not satisfied within {waitTimeout}.");
    }
}
