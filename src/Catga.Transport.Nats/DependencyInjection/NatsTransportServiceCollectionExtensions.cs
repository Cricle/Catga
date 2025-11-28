using Catga.Abstractions;
using Catga.Configuration;
using Catga.Observability;
using Catga.Resilience;
using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using System.Diagnostics;

namespace Catga.DependencyInjection;

/// <summary>NATS transport DI extensions (serializer-agnostic)</summary>
public static class NatsTransportServiceCollectionExtensions
{
    /// <summary>
    /// Add NATS message transport (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddNatsTransport(this IServiceCollection services, Action<NatsTransportOptions>? configure = null)
    {
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Transport.NATS");
        try
        {
            var options = new NatsTransportOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);

            services.TryAddSingleton<IMessageTransport>(sp => new NatsMessageTransport(sp.GetRequiredService<INatsConnection>(),
                                                                                       sp.GetRequiredService<IMessageSerializer>(),
                                                                                       sp.GetRequiredService<ILogger<NatsMessageTransport>>(),
                                                                                       sp.GetRequiredService<CatgaOptions>(),
                                                                                       options,
                                                                                       sp.GetRequiredService<IResiliencePipelineProvider>()));
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

