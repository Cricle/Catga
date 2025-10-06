using Catga.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// 事件溯源扩展方法
/// </summary>
public static class EventSourcingExtensions
{
    /// <summary>
    /// 添加内存事件存储（用于测试）
    /// </summary>
    public static IServiceCollection AddMemoryEventStore(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventStore, MemoryEventStore>();
        return services;
    }

    /// <summary>
    /// 添加投影管理器
    /// </summary>
    public static IServiceCollection AddProjectionManager(this IServiceCollection services)
    {
        services.TryAddSingleton<IProjectionManager, ProjectionManager>();
        services.AddHostedService(sp => (ProjectionManager)sp.GetRequiredService<IProjectionManager>());
        return services;
    }

    /// <summary>
    /// 注册投影
    /// </summary>
    public static IServiceCollection AddProjection<TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        services.AddSingleton<IProjection, TProjection>();
        return services;
    }

    /// <summary>
    /// 添加事件溯源支持
    /// </summary>
    public static IServiceCollection AddEventSourcing(this IServiceCollection services)
    {
        services.AddMemoryEventStore();
        services.AddProjectionManager();
        return services;
    }
}

