using Catga.Abstractions;
using Catga.Configuration;
using Catga.Resilience;
using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.DependencyInjection;

/// <summary>NATS transport DI extensions (serializer-agnostic)</summary>
public static class NatsTransportServiceCollectionExtensions
{
    /// <summary>Add NATS message transport (requires IMessageSerializer to be registered separately)</summary>
    public static IServiceCollection AddNatsTransport(this IServiceCollection services, Action<NatsTransportOptions>? configure = null)
    {
        var options = new NatsTransportOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport>(sp => new NatsMessageTransport(
            sp.GetRequiredService<INatsConnection>(),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<ILogger<NatsMessageTransport>>(),
            sp.GetRequiredService<IResiliencePipelineProvider>(),
            sp.GetRequiredService<CatgaOptions>(),
            options));
        return services;
    }

    public static IServiceCollection AddNatsTransport(this IServiceCollection services, NatsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= new NatsTransportOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport>(sp => new NatsMessageTransport(
            sp.GetRequiredService<INatsConnection>(),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<ILogger<NatsMessageTransport>>(),
            sp.GetRequiredService<IResiliencePipelineProvider>(),
            sp.GetRequiredService<CatgaOptions>(),
            options));
        return services;
    }

    /// <summary>Add NATS transport with URL</summary>
    public static IServiceCollection AddNatsTransport(this IServiceCollection services, string natsUrl)
    {
        services.TryAddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts { Url = natsUrl }));
        return services.AddNatsTransport(configure: null);
    }
}
