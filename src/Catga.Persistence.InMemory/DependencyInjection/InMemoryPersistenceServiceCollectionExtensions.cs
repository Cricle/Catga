using Catga.Persistence.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Catga;

/// <summary>
/// Convenience extensions for Catga InMemory Persistence (aggregates all persistence features)
/// </summary>
public static class InMemoryPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 注册所有 InMemory Persistence 相关组件（Event Store + Repository）
    /// </summary>
    /// <remarks>
    /// 这是一个便利扩展，等同于：
    /// <code>
    /// services.AddInMemoryEventStore();
    /// services.AddEventStoreRepository();
    /// </code>
    /// 
    /// <para>
    /// 包含的组件：
    /// - IEventStore - 内存事件存储
    /// - IEventStoreRepository - 聚合存储库
    /// </para>
    /// 
    /// <para>
    /// 对于生产环境，建议使用：
    /// - Catga.Persistence.Nats - NATS JetStream 持久化
    /// - Catga.Persistence.Redis - Redis Streams 持久化
    /// </para>
    /// 
    /// <para>
    /// 注意：InMemory 实现不提供跨进程共享，仅适用于：
    /// - 单元测试
    /// - 集成测试
    /// - 单节点开发环境
    /// </para>
    /// </remarks>
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册 Event Store
        services.AddInMemoryEventStore();
        
        // 注册 Event Store Repository
        services.AddEventStoreRepository();

        return services;
    }
}

