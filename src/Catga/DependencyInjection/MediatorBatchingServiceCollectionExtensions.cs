using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

public static class MediatorBatchingServiceCollectionExtensions
{
    // ========== Mediator Batching Configuration ==========

    public static CatgaServiceBuilder UseMediatorAutoBatchingProfilesFromAssembly(this CatgaServiceBuilder builder)
    {
        // Ensure provider is available so behaviors can resolve typed overrides
        builder.Services.TryAddSingleton<IMediatorBatchOptionsProvider, DefaultMediatorBatchOptionsProvider>();
        // Registrations are provided via source generator ModuleInitializer and applied by AddCatga() bootstrap.
        return builder;
    }
}
