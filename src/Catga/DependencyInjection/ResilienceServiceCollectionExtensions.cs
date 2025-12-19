using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga.Observability;
using Catga.Resilience;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Medallion.Threading;
using Medallion.Threading.WaitHandles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>DI extensions for Polly-based resilience (supports .NET 6 via Polly v7, .NET 8+ via Polly v8)</summary>
public static class ResilienceServiceCollectionExtensions
{
    // ========== Resilience Service Registration ==========

    public static IServiceCollection AddCatgaResilience(this IServiceCollection services, Action<CatgaResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Resilience");
        try
        {
            var options = new CatgaResilienceOptions();
            configure?.Invoke(options);
#if NET8_0_OR_GREATER
            // If user didn't set Persistence bulkhead, apply conservative defaults when resilience is enabled
            if (options.PersistenceBulkheadConcurrency <= 0)
            {
                var c = Math.Max(Environment.ProcessorCount * 2, 16);
                options.PersistenceBulkheadConcurrency = c;
                options.PersistenceBulkheadQueueLimit = c;
            }
#endif
            // Use AddSingleton (not TryAdd) so that explicit UseResilience registrations override any prior defaults
            services.AddSingleton(options);
            services.AddSingleton<IResiliencePipelineProvider>(sp => new DefaultResiliencePipelineProvider(options));
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(PollyBehavior<,>));

            // Register attribute-driven behavior for [Idempotent], [DistributedLock], [Retry], [Timeout], [CircuitBreaker]
            // Use WaitHandleDistributedSynchronizationProvider as default in-memory lock provider
            services.TryAddSingleton<IDistributedLockProvider>(new WaitHandleDistributedSynchronizationProvider());
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(AttributeDrivenBehavior<,>));

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
