using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Catga.Abstractions;
using NATS.Client.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga.Observability;

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
        var tags = new TagList { { "component", "DI.Transport.NATS" } };
        try
        {
            var options = new NatsTransportOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);

            // Use factory to pass CatgaOptions so global naming conventions can apply
            services.TryAddSingleton<IMessageTransport>(sp =>
            {
                var conn = sp.GetRequiredService<INatsConnection>();
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                var logger = sp.GetRequiredService<ILogger<NatsMessageTransport>>();
                var catgaOptions = sp.GetRequiredService<Catga.Configuration.CatgaOptions>();
                var provider = sp.GetRequiredService<Catga.Resilience.IResiliencePipelineProvider>();
                return new NatsMessageTransport(conn, serializer, logger, catgaOptions, options, provider);
            });
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tags);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tags);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            throw;
        }
    }
}

