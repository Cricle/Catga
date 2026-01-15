using Catga.DependencyInjection;
using Catga.Persistence.InMemory;
using Catga.Persistence.Nats;
using Catga.Persistence.Redis;
using Catga.Tests.PropertyTests;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Tests.Framework;

/// <summary>
/// 后端矩阵测试框架
/// 支持生成和配置所有 27 种后端组合 (3^3)
/// </summary>
public static class BackendMatrixTestFramework
{
    /// <summary>
    /// 获取所有 27 种后端组合
    /// </summary>
    public static IEnumerable<(BackendType eventStore, BackendType transport, BackendType flowStore)> GetAllCombinations()
    {
        var backends = new[] { BackendType.InMemory, BackendType.Redis, BackendType.Nats };
        
        return from es in backends
               from t in backends
               from fs in backends
               select (es, t, fs);
    }

    /// <summary>
    /// 获取所有后端组合作为测试数据（用于 Theory 测试）
    /// </summary>
    public static IEnumerable<object[]> GetAllCombinationsAsTestData()
    {
        return GetAllCombinations()
            .Select(combo => new object[] { combo.eventStore, combo.transport, combo.flowStore });
    }

    /// <summary>
    /// 配置指定的后端组合
    /// </summary>
    public static IServiceCollection ConfigureBackends(
        this IServiceCollection services,
        BackendType eventStore,
        BackendType transport,
        BackendType flowStore,
        string? redisConnectionString = null,
        string? natsConnectionString = null)
    {
        // Register IMessageSerializer first (required by Redis and NATS backends)
        services.UseMemoryPackSerializer();

        // Register connections first
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            services.AddRedisPersistence(redisConnectionString);
        }

        if (!string.IsNullOrEmpty(natsConnectionString))
        {
            services.AddNatsTransport(natsConnectionString); // This registers INatsConnection
        }

        // Configure EventStore
        ConfigureEventStore(services, eventStore);
        
        // Configure SnapshotStore (same backend as EventStore)
        ConfigureSnapshotStore(services, eventStore);

        // Configure Transport
        ConfigureTransport(services, transport);

        // Configure FlowStore
        ConfigureFlowStore(services, flowStore);

        return services;
    }

    /// <summary>
    /// 配置 EventStore
    /// </summary>
    private static void ConfigureEventStore(
        IServiceCollection services,
        BackendType backend)
    {
        switch (backend)
        {
            case BackendType.InMemory:
                services.AddInMemoryEventStore();
                break;

            case BackendType.Redis:
                services.AddRedisEventStore();
                break;

            case BackendType.Nats:
                services.AddNatsEventStore();
                break;

            default:
                throw new ArgumentException($"Unknown backend type: {backend}");
        }
    }
    
    /// <summary>
    /// 配置 SnapshotStore
    /// </summary>
    private static void ConfigureSnapshotStore(
        IServiceCollection services,
        BackendType backend)
    {
        switch (backend)
        {
            case BackendType.InMemory:
                services.AddInMemorySnapshotStore();
                break;

            case BackendType.Redis:
                services.AddRedisSnapshotStore();
                break;

            case BackendType.Nats:
                services.AddNatsSnapshotStore();
                break;

            default:
                throw new ArgumentException($"Unknown backend type: {backend}");
        }
    }

    /// <summary>
    /// 配置 Transport
    /// </summary>
    private static void ConfigureTransport(
        IServiceCollection services,
        BackendType backend)
    {
        switch (backend)
        {
            case BackendType.InMemory:
                services.AddInMemoryTransport();
                break;

            case BackendType.Redis:
                services.AddRedisTransport();
                break;

            case BackendType.Nats:
                services.AddNatsTransport();
                break;

            default:
                throw new ArgumentException($"Unknown backend type: {backend}");
        }
    }

    /// <summary>
    /// 配置 FlowStore
    /// </summary>
    private static void ConfigureFlowStore(
        IServiceCollection services,
        BackendType backend)
    {
        switch (backend)
        {
            case BackendType.InMemory:
                services.AddInMemoryFlowStore();
                break;

            case BackendType.Redis:
                services.AddRedisFlowStore();
                break;

            case BackendType.Nats:
                services.AddNatsFlowStore();
                break;

            default:
                throw new ArgumentException($"Unknown backend type: {backend}");
        }
    }

    /// <summary>
    /// 获取后端组合的描述性名称
    /// </summary>
    public static string GetCombinationName(BackendType eventStore, BackendType transport, BackendType flowStore)
    {
        return $"{eventStore}+{transport}+{flowStore}";
    }

    /// <summary>
    /// 检查后端组合是否需要 Docker
    /// </summary>
    public static bool RequiresDocker(BackendType eventStore, BackendType transport, BackendType flowStore)
    {
        return eventStore != BackendType.InMemory ||
               transport != BackendType.InMemory ||
               flowStore != BackendType.InMemory;
    }

    /// <summary>
    /// 获取后端组合需要的连接字符串类型
    /// </summary>
    public static (bool needsRedis, bool needsNats) GetRequiredConnections(
        BackendType eventStore,
        BackendType transport,
        BackendType flowStore)
    {
        var needsRedis = eventStore == BackendType.Redis ||
                        transport == BackendType.Redis ||
                        flowStore == BackendType.Redis;

        var needsNats = eventStore == BackendType.Nats ||
                       transport == BackendType.Nats ||
                       flowStore == BackendType.Nats;

        return (needsRedis, needsNats);
    }
}

/// <summary>
/// 后端矩阵测试基类
/// 提供跨所有后端组合的测试支持
/// </summary>
public abstract class BackendMatrixTestBase : IAsyncLifetime
{
    protected BackendType EventStoreBackend { get; private set; }
    protected BackendType TransportBackend { get; private set; }
    protected BackendType FlowStoreBackend { get; private set; }

    protected IServiceProvider ServiceProvider { get; private set; } = null!;
    protected BackendTestFixture? RedisFixture { get; private set; }
    protected BackendTestFixture? NatsFixture { get; private set; }

    /// <summary>
    /// 配置后端组合
    /// </summary>
    protected void ConfigureBackends(BackendType eventStore, BackendType transport, BackendType flowStore)
    {
        EventStoreBackend = eventStore;
        TransportBackend = transport;
        FlowStoreBackend = flowStore;
    }

    /// <summary>
    /// 配置服务
    /// 子类可以重写此方法来添加额外的服务配置
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // 默认不添加任何服务
    }

    public virtual async Task InitializeAsync()
    {
        // 检查是否需要 Docker
        var (needsRedis, needsNats) = BackendMatrixTestFramework.GetRequiredConnections(
            EventStoreBackend, TransportBackend, FlowStoreBackend);

        // 初始化 Redis 容器
        if (needsRedis)
        {
            RedisFixture = new BackendTestFixture(BackendType.Redis);
            await RedisFixture.InitializeAsync();
            
            if (!RedisFixture.IsDockerAvailable)
            {
                // Docker not available - tests will be skipped
                return;
            }
        }

        // 初始化 NATS 容器
        if (needsNats)
        {
            NatsFixture = new BackendTestFixture(BackendType.Nats);
            await NatsFixture.InitializeAsync();
            
            if (!NatsFixture.IsDockerAvailable)
            {
                // Docker not available - tests will be skipped
                return;
            }
        }

        // 配置服务
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        services.ConfigureBackends(
            EventStoreBackend,
            TransportBackend,
            FlowStoreBackend,
            RedisFixture?.RedisConnectionString,
            NatsFixture?.NatsConnectionString);

        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();

        await OnInitializedAsync();
    }

    /// <summary>
    /// 初始化完成后的回调
    /// </summary>
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        await OnDisposingAsync();

        if (ServiceProvider is IAsyncDisposable asyncSp)
            await asyncSp.DisposeAsync();
        else if (ServiceProvider is IDisposable sp)
            sp.Dispose();

        if (RedisFixture != null)
            await RedisFixture.DisposeAsync();

        if (NatsFixture != null)
            await NatsFixture.DisposeAsync();
    }

    /// <summary>
    /// 清理前的回调
    /// </summary>
    protected virtual Task OnDisposingAsync() => Task.CompletedTask;
}
