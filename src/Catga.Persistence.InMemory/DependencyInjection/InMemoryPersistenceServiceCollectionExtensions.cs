using Microsoft.Extensions.DependencyInjection;

namespace Catga;

/// <summary>
/// Convenience extensions for Catga InMemory Persistence (aggregates all persistence features)
/// </summary>
public static class InMemoryPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Placeholder for future aggregated InMemory Persistence registration
    /// </summary>
    /// <remarks>
    /// 当前，请使用具体的注册扩展：
    /// - AddInMemoryEventSourcing() - Event Store
    /// - AddDistributedMemoryCache() - 分布式缓存
    /// - AddInMemoryOutboxPublisher() - Outbox 模式
    /// - AddInMemoryInboxProcessor() - Inbox 模式
    /// 
    /// 这些扩展在各自的 ServiceCollectionExtensions 文件中定义。
    /// </remarks>
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TODO: 聚合所有 Persistence 相关的注册
        // 目前，用户需要单独调用各自的扩展方法
        
        return services;
    }
}

