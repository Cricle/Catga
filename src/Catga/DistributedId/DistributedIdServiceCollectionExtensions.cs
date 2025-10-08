using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DistributedId;

/// <summary>
/// Distributed ID service collection extensions
/// </summary>
public static class DistributedIdServiceCollectionExtensions
{
    /// <summary>
    /// Add distributed ID generator
    /// </summary>
    public static IServiceCollection AddDistributedId(
        this IServiceCollection services,
        Action<DistributedIdOptions>? configure = null)
    {
        var options = new DistributedIdOptions();
        configure?.Invoke(options);
        options.Validate();

        var workerId = options.GetWorkerId();

        services.TryAddSingleton<IDistributedIdGenerator>(
            _ => new SnowflakeIdGenerator(workerId));

        return services;
    }

    /// <summary>
    /// Add distributed ID generator with explicit worker ID
    /// </summary>
    public static IServiceCollection AddDistributedId(
        this IServiceCollection services,
        int workerId)
    {
        if (workerId < 0 || workerId > 1023)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerId),
                "Worker ID must be between 0 and 1023");
        }

        services.TryAddSingleton<IDistributedIdGenerator>(
            _ => new SnowflakeIdGenerator(workerId));

        return services;
    }
}

