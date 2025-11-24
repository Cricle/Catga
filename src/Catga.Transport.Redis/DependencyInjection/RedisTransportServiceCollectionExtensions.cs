using Catga.Abstractions;
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

        // Build ConfigurationOptions from RedisTransportOptions
        var configOptions = CreateRedisConfiguration(options);

        // Register ConnectionMultiplexer as singleton
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            return ConnectionMultiplexer.Connect(configOptions);
        });

        // Register Transport
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            // Pass options so ChannelPrefix/Naming can take effect
            var catgaOptions = sp.GetRequiredService<Catga.Configuration.CatgaOptions>();
            if (options.Naming is null && catgaOptions.EndpointNamingConvention is not null)
                options.Naming = catgaOptions.EndpointNamingConvention;
            return new RedisMessageTransport(
                redis,
                serializer,
                options,
                options.ConsumerGroup,
                options.ConsumerName);
        });

        return services;
    }

    /// <summary>
    /// Create Redis ConfigurationOptions from RedisTransportOptions
    /// </summary>
    private static ConfigurationOptions CreateRedisConfiguration(RedisTransportOptions options)
    {
        var config = ConfigurationOptions.Parse(options.ConnectionString);

        // Connection settings
        config.ConnectTimeout = options.ConnectTimeout;
        config.SyncTimeout = options.SyncTimeout;
        config.AsyncTimeout = options.AsyncTimeout;
        config.AbortOnConnectFail = options.AbortOnConnectFail;
        config.ClientName = options.ClientName;
        config.AllowAdmin = options.AllowAdmin;

        // Performance settings
        config.KeepAlive = options.KeepAlive;
        config.ConnectRetry = options.ConnectRetry;
        // Note: RespectAsyncTimeout is a typo in some versions, using the correct property name

        // Database
        config.DefaultDatabase = options.DefaultDatabase;

        // SSL/TLS
        if (options.UseSsl)
        {
            config.Ssl = true;
            if (!string.IsNullOrEmpty(options.SslHost))
            {
                config.SslHost = options.SslHost;
            }
        }

        // Sentinel mode
        if (options.Mode == RedisMode.Sentinel && !string.IsNullOrEmpty(options.SentinelServiceName))
        {
            config.ServiceName = options.SentinelServiceName;
        }

        return config;
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

