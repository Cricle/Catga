using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// 后端类型枚举
/// </summary>
public enum BackendType
{
    /// <summary>内存后端</summary>
    InMemory,
    /// <summary>Redis 后端</summary>
    Redis,
    /// <summary>NATS 后端</summary>
    Nats
}

/// <summary>
/// 全局共享的测试容器基础设施
/// 所有测试共享同一个Redis和NATS容器实例，大幅提升测试速度
/// </summary>
public sealed class SharedTestContainers
{
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static SharedTestContainers? _instance;
    private static bool _isInitialized = false;

    private RedisContainer? _redisContainer;
    private IContainer? _natsContainer;

    public string? RedisConnectionString { get; private set; }
    public string? NatsConnectionString { get; private set; }
    public bool IsDockerAvailable { get; private set; }

    private SharedTestContainers() { }

    public static SharedTestContainers Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SharedTestContainers();
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

            Console.WriteLine("🚀 Initializing shared test containers...");

            // Fix Docker endpoint for Windows - Testcontainers has a bug with npipe URI format
            if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe://./pipe/docker_engine");
                Console.WriteLine("✓ Set DOCKER_HOST for Windows: npipe://./pipe/docker_engine");
            }

            // 检查 Docker 是否可用
            IsDockerAvailable = await CheckDockerAvailableAsync();
            if (!IsDockerAvailable)
            {
                Console.WriteLine("⚠ Docker not available, tests will use InMemory implementations");
                _isInitialized = true;
                return;
            }

            // 启动 Redis 容器
            await InitializeRedisAsync();

            // 启动 NATS 容器
            await InitializeNatsAsync();

            _isInitialized = true;
            Console.WriteLine("✓ Shared test containers ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to initialize containers: {ex.Message}");
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
            _redisContainer = new RedisBuilder()
                .WithImage("redis:7-alpine")
                .WithName($"catga-test-redis-{Guid.NewGuid():N}")
                .WithCommand("redis-server", "--save", "", "--appendonly", "no")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                .WithCleanUp(true)
                .Build();

            await _redisContainer.StartAsync();
            RedisConnectionString = _redisContainer.GetConnectionString();
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
            _natsContainer = new ContainerBuilder()
                .WithImage("nats:2.10-alpine")
                .WithName($"catga-test-nats-{Guid.NewGuid():N}")
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
            Console.WriteLine($"✓ NATS container started: {NatsConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ NATS container failed: {ex.Message}");
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
    /// 生成唯一的键前缀用于测试隔离
    /// </summary>
    public static string GenerateKeyPrefix(string testName)
    {
        return $"test:{testName}:{Guid.NewGuid():N}:";
    }
}

/// <summary>
/// 后端测试夹具 - 使用共享容器
/// </summary>
public class BackendTestFixture : IAsyncLifetime
{
    private readonly BackendType _backendType;
    private readonly SharedTestContainers _sharedContainers;

    public string? RedisConnectionString => _sharedContainers.RedisConnectionString;
    public string? NatsConnectionString => _sharedContainers.NatsConnectionString;
    public BackendType BackendType => _backendType;
    public bool IsDockerAvailable => _sharedContainers.IsDockerAvailable;

    public BackendTestFixture(BackendType backendType)
    {
        _backendType = backendType;
        _sharedContainers = SharedTestContainers.Instance;
    }

    public async Task InitializeAsync()
    {
        await _sharedContainers.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        // 不释放共享容器，让它们在整个测试会话中保持运行
        return Task.CompletedTask;
    }
}

/// <summary>
/// Redis 集合定义（用于 xUnit 集合夹具）
/// </summary>
[CollectionDefinition("Redis", DisableParallelization = true)]
public class RedisCollection : ICollectionFixture<RedisTestFixture>
{
}

/// <summary>
/// NATS 集合定义（用于 xUnit 集合夹具）
/// </summary>
[CollectionDefinition("Nats", DisableParallelization = true)]
public class NatsCollection : ICollectionFixture<NatsTestFixture>
{
}

/// <summary>
/// Redis 测试夹具（用于 xUnit 集合）
/// </summary>
public class RedisTestFixture : BackendTestFixture
{
    public RedisTestFixture() : base(BackendType.Redis)
    {
    }
}

/// <summary>
/// NATS 测试夹具（用于 xUnit 集合）
/// </summary>
public class NatsTestFixture : BackendTestFixture
{
    public NatsTestFixture() : base(BackendType.Nats)
    {
    }
}

/// <summary>
/// 跳过测试的辅助类
/// </summary>
public static class SkipHelper
{
    /// <summary>
    /// 如果 Docker 不可用则跳过测试
    /// </summary>
    public static void SkipIfDockerNotAvailable(BackendTestFixture? fixture)
    {
        if (fixture == null || !fixture.IsDockerAvailable)
        {
            Skip.If(true, "Docker is not available. Skipping integration test.");
        }
    }

    /// <summary>
    /// 如果后端不是指定类型则跳过测试
    /// </summary>
    public static void SkipIfNotBackend(BackendTestFixture? fixture, BackendType expectedBackend)
    {
        if (fixture?.BackendType != expectedBackend)
        {
            Skip.If(true, $"Test requires {expectedBackend} backend.");
        }
    }
}
