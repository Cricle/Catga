using Catga.Abstractions;
using Catga.Configuration;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using StackExchange.Redis.MultiplexerPool;

namespace Catga;

/// <summary>DI extensions for Redis Transport</summary>
public static class RedisTransportServiceCollectionExtensions
{
    /// <summary>注册 Redis Transport (支持 Pub/Sub 和 Streams)</summary>
    public static IServiceCollection AddRedisTransport(this IServiceCollection services, Action<RedisTransportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RedisTransportOptions();
        configure?.Invoke(options);

        if (options.RegistConnection)
        {
            services.TryAddSingleton<IConnectionMultiplexerPool>(sp =>
            {
                var configString = options.ConfigurationOptions?.ToString() ?? "localhost:6379";
                var strategy = options.SelectionStrategy == Transport.ConnectionSelectionStrategy.LoadBased
                    ? StackExchange.Redis.MultiplexerPool.ConnectionSelectionStrategy.LeastLoaded
                    : StackExchange.Redis.MultiplexerPool.ConnectionSelectionStrategy.RoundRobin;
                return ConnectionMultiplexerPoolFactory.Create(poolSize: options.PoolSize, configuration: configString, connectionSelectionStrategy: strategy);
            });
            services.TryAddSingleton<IConnectionMultiplexer>(sp =>
                sp.GetRequiredService<IConnectionMultiplexerPool>().GetAsync().GetAwaiter().GetResult().Connection);
        }

        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var pool = sp.GetRequiredService<IConnectionMultiplexerPool>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var catgaOptions = sp.GetRequiredService<CatgaOptions>();
            if (options.Naming is null && catgaOptions.EndpointNamingConvention is not null)
                options.Naming = catgaOptions.EndpointNamingConvention;
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisMessageTransport(pool, serializer, provider, options, options.ConsumerGroup, options.ConsumerName);
        });
        return services;
    }

    /// <summary>Add Redis Transport with connection string</summary>
    public static IServiceCollection AddRedisTransport(this IServiceCollection services, string connectionString)
        => services.AddRedisTransport(opts => opts.ConfigurationOptions = ConfigurationOptions.Parse(connectionString));

    /// <summary>注册 Redis Transport (使用已有的 ConnectionMultiplexer)</summary>
    public static IServiceCollection AddRedisTransport(this IServiceCollection services, IConnectionMultiplexer connectionMultiplexer, string? consumerGroup = null, string? consumerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        services.TryAddSingleton(connectionMultiplexer);
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisMessageTransport(connectionMultiplexer, serializer, provider, null, consumerGroup, consumerName);
        });
        return services;
    }
}
