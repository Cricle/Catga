using Catga.DependencyInjection;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence.InMemory.DependencyInjection;

/// <summary>
/// Extension methods for in-memory distributed features.
/// </summary>
public static class InMemoryDistributedExtensions
{
    /// <summary>Add file-based distributed lock (for single-node or testing) using DistributedLock.FileSystem.</summary>
    public static CatgaServiceBuilder UseFileSystemDistributedLock(this CatgaServiceBuilder builder, string? lockDirectory = null)
    {
        var dir = new DirectoryInfo(lockDirectory ?? Path.Combine(Path.GetTempPath(), "catga-locks"));
        if (!dir.Exists)
            dir.Create();

        builder.Services.TryAddSingleton<IDistributedLockProvider>(new FileDistributedSynchronizationProvider(dir));
        return builder;
    }
}
