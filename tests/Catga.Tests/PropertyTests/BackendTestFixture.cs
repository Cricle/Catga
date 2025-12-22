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
/// 后端测试夹具
/// 管理 Redis 和 NATS 容器的生命周期
/// </summary>
public class BackendTestFixture : IAsyncLifetime
{
    private readonly BackendType _backendType;
    private RedisContainer? _redisContainer;
    private IContainer? _natsContainer;

    /// <summary>
    /// Redis 连接字符串（仅 Redis 后端可用）
    /// </summary>
    public string? RedisConnectionString { get; private set; }

    /// <summary>
    /// NATS 连接字符串（仅 NATS 后端可用）
    /// </summary>
    public string? NatsConnectionString { get; private set; }

    /// <summary>
    /// 当前后端类型
    /// </summary>
    public BackendType BackendType => _backendType;

    /// <summary>
    /// Docker 是否可用
    /// </summary>
    public bool IsDockerAvailable { get; private set; }

    public BackendTestFixture(BackendType backendType)
    {
        _backendType = backendType;
    }

    public async Task InitializeAsync()
    {
        // 检查 Docker 是否可用
        IsDockerAvailable = await CheckDockerAvailableAsync();
        if (!IsDockerAvailable)
        {
            return;
        }

        switch (_backendType)
        {
            case BackendType.Redis:
                await InitializeRedisAsync();
                break;
            case BackendType.Nats:
                await InitializeNatsAsync();
                break;
            case BackendType.InMemory:
                // InMemory 不需要容器
                break;
        }
    }

    public async Task DisposeAsync()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }

        if (_natsContainer != null)
        {
            await _natsContainer.DisposeAsync();
        }
    }

    private async Task InitializeRedisAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();
        RedisConnectionString = _redisContainer.GetConnectionString();
    }

    private async Task InitializeNatsAsync()
    {
        _natsContainer = new ContainerBuilder()
            .WithImage("nats:2.10-alpine")
            .WithCommand("-js") // Enable JetStream
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
            .Build();

        await _natsContainer.StartAsync();
        var host = _natsContainer.Hostname;
        var port = _natsContainer.GetMappedPublicPort(4222);
        NatsConnectionString = $"nats://{host}:{port}";
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
}

/// <summary>
/// Redis 集合定义（用于 xUnit 集合夹具）
/// </summary>
[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisTestFixture>
{
}

/// <summary>
/// NATS 集合定义（用于 xUnit 集合夹具）
/// </summary>
[CollectionDefinition("Nats")]
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
