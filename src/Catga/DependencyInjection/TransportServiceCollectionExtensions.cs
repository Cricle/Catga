using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// 传输层服务注册扩展
/// </summary>
public static class TransportServiceCollectionExtensions
{
    /// <summary>
    /// 添加消息传输服务
    /// </summary>
    public static IServiceCollection AddMessageTransport<TTransport>(
        this IServiceCollection services)
        where TTransport : class, IMessageTransport
    {
        services.TryAddSingleton<IMessageTransport, TTransport>();
        return services;
    }

    /// <summary>
    /// 添加内存消息传输（用于测试和本地开发）
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(
        this IServiceCollection services)
    {
        return services.AddMessageTransport<InMemoryMessageTransport>();
    }
}

