using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Catga.Abstractions;

namespace Catga.DependencyInjection;

/// <summary>NATS transport DI extensions (serializer-agnostic)</summary>
public static class NatsTransportServiceCollectionExtensions
{
    /// <summary>
    /// Add NATS message transport (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddNatsTransport(this IServiceCollection services, Action<NatsTransportOptions>? configure = null)
    {
        var options = new NatsTransportOptions();


        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport, NatsMessageTransport>();
        return services;
    }
}

