using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// NATS 传输层服务注册扩展
/// </summary>
public static class NatsTransportServiceCollectionExtensions
{
    /// <summary>
    /// 添加 NATS 消息传输
    /// </summary>
    public static IServiceCollection AddNatsTransport(
        this IServiceCollection services,
        Action<NatsTransportOptions>? configure = null)
    {
        var options = new NatsTransportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport, NatsMessageTransport>();

        return services;
    }
}

