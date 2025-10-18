using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Catga;

/// <summary>
/// DI extensions for Redis Transport
/// </summary>
public static class RedisTransportServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Redis Transport (支持 Pub/Sub 和 Streams)
    /// </summary>
    public static IServiceCollection AddRedisTransport(
        this IServiceCollection services,
        Action<RedisTransportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        var options = new RedisTransportOptions();
        configure?.Invoke(options);

        // Register ConnectionMultiplexer as singleton
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            return ConnectionMultiplexer.Connect(options.ConnectionString);
        });

        // Register Transport
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new RedisMessageTransport(
                redis,
                serializer,
                options.ConsumerGroup,
                options.ConsumerName);
        });

        return services;
    }

    /// <summary>
    /// 注册 Redis Transport (使用已有的 ConnectionMultiplexer)
    /// </summary>
    public static IServiceCollection AddRedisTransport(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        string? consumerGroup = null,
        string? consumerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        services.TryAddSingleton(connectionMultiplexer);

        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new RedisMessageTransport(
                connectionMultiplexer,
                serializer,
                consumerGroup,
                consumerName);
        });

        return services;
    }
}

