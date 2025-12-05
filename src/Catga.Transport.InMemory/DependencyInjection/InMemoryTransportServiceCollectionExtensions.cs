using Catga.Configuration;
using Catga.Observability;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Catga;

/// <summary>DI extensions for InMemory Transport</summary>
public static class InMemoryTransportServiceCollectionExtensions
{
    /// <summary>
    /// 注册 InMemory Transport (用于开发/测试)
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Transport.InMemory");
        try
        {
            // 注册 Transport (InMemoryIdempotencyStore 是内部实现)
            services.TryAddSingleton<IMessageTransport>(sp =>
            {
                var logger = sp.GetService<ILogger<InMemoryMessageTransport>>();
                var global = sp.GetRequiredService<CatgaOptions>();
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new InMemoryMessageTransport(logger, provider, global);
            });

            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
            throw;
        }
    }
}

