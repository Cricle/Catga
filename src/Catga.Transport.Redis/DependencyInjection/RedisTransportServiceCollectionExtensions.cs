using Catga.Transport;
using Catga.Transport.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Redis 传输层服务注册扩展
/// </summary>
public static class RedisTransportServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Redis 消息传输
    /// </summary>
    public static IServiceCollection AddRedisTransport(
        this IServiceCollection services,
        Action<RedisTransportOptions>? configure = null)
    {
        var options = new RedisTransportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport, RedisMessageTransport>();

        return services;
    }
}

