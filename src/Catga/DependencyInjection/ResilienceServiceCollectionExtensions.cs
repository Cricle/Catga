using Catga.Resilience;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Medallion.Threading;
using Medallion.Threading.WaitHandles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>DI extensions for Polly-based resilience</summary>
public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddCatgaResilience(this IServiceCollection services, Action<CatgaResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CatgaResilienceOptions();
        configure?.Invoke(options);

#if NET8_0_OR_GREATER
        if (options.PersistenceBulkheadConcurrency <= 0)
        {
            var c = Math.Max(Environment.ProcessorCount * 2, 16);
            options.PersistenceBulkheadConcurrency = c;
            options.PersistenceBulkheadQueueLimit = c;
        }
#endif

        services.AddSingleton(options);
        services.AddSingleton<IResiliencePipelineProvider>(sp => new DefaultResiliencePipelineProvider(options));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(PollyBehavior<,>));
        services.TryAddSingleton<IDistributedLockProvider>(new WaitHandleDistributedSynchronizationProvider());
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(AttributeDrivenBehavior<,>));

        return services;
    }
}
