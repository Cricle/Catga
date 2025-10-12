using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>NATS transport DI extensions</summary>
public static class NatsTransportServiceCollectionExtensions
{
    public static IServiceCollection AddNatsTransport(this IServiceCollection services, Action<NatsTransportOptions>? configure = null)
    {
        var options = new NatsTransportOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport, NatsMessageTransport>();
        return services;
    }
}

