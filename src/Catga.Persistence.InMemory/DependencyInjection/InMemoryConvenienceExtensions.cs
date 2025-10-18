using Microsoft.Extensions.DependencyInjection;

namespace Catga;

/// <summary>
/// Convenience extensions for Catga InMemory (aggregates Transport + Persistence)
/// </summary>
public static class InMemoryConvenienceExtensions
{
    /// <summary>
    /// 注册所有 InMemory 实现（Transport + Persistence）- 开发/测试用便利方法
    /// </summary>
    /// <remarks>
    /// 这是一个便利扩展，等同于：
    /// <code>
    /// services.AddInMemoryTransport();
    /// services.AddInMemoryPersistence();
    /// </code>
    ///
    /// 对于生产环境，建议分别注册所需的组件。
    /// 
    /// 注意：要使用此方法，需要同时引用 Catga.Transport.InMemory 和 Catga.Persistence.InMemory
    /// </remarks>
    public static IServiceCollection AddCatgaInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册 Transport
        services.AddInMemoryTransport();

        // 注册 Persistence
        services.AddInMemoryPersistence();

        return services;
    }
}

