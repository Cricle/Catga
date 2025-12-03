using Catga.Abstractions;
using Catga.Configuration;
using Catga.Observability;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using System.Diagnostics;

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
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Transport.Redis");
        try
        {
            // Configure options
            var options = new RedisTransportOptions();
            configure?.Invoke(options);

            if (options.RegistConnection)
                services.TryAddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(options.ConfigurationOptions ?? new ConfigurationOptions()));

            // Register Transport
            services.TryAddSingleton<IMessageTransport>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                // Pass options so ChannelPrefix/Naming can take effect
                var catgaOptions = sp.GetRequiredService<CatgaOptions>();
                if (options.Naming is null && catgaOptions.EndpointNamingConvention is not null)
                    options.Naming = catgaOptions.EndpointNamingConvention;
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new RedisMessageTransport(
                    redis,
                    serializer,
                    provider,
                    options,
                    options.ConsumerGroup,
                    options.ConsumerName);
            });

            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
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
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Transport.Redis");
        try
        {
            services.TryAddSingleton(connectionMultiplexer);

            services.TryAddSingleton<IMessageTransport>(sp =>
            {
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new RedisMessageTransport(
                    connectionMultiplexer,
                    serializer,
                    provider,
                    null,
                    consumerGroup,
                    consumerName);
            });

            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
    }
}

