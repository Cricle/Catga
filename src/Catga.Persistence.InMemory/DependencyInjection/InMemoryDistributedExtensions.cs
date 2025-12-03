using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Persistence.InMemory.Locking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence.InMemory.DependencyInjection;

/// <summary>
/// Extension methods for in-memory distributed features.
/// </summary>
public static class InMemoryDistributedExtensions
{
    /// <summary>Add in-memory distributed lock (for single-node or testing).</summary>
    public static CatgaServiceBuilder UseInMemoryDistributedLock(this CatgaServiceBuilder builder)
    {
        builder.Services.TryAddSingleton<IDistributedLock, InMemoryDistributedLock>();
        return builder;
    }

    /// <summary>Add in-memory distributed lock with options.</summary>
    public static CatgaServiceBuilder UseInMemoryDistributedLock(
        this CatgaServiceBuilder builder,
        Action<DistributedLockOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IDistributedLock, InMemoryDistributedLock>();
        return builder;
    }
}
