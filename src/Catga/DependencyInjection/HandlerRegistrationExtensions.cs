// Default empty implementation - will be extended by Source Generator in consuming projects
#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Base class for handler registration extensions.
/// Source Generator in consuming projects will generate partial methods to fill these.
/// </summary>
public static partial class CatgaGeneratedHandlerRegistrations
{
    /// <summary>
    /// Registers all handlers detected by Source Generator.
    /// Override this in consuming projects using Source Generator.
    /// </summary>
    public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)
    {
        // Default implementation does nothing
        // Source Generator will create a partial class with actual registrations
        return services;
    }
}

/// <summary>
/// Base class for service registration extensions.
/// Source Generator in consuming projects will generate partial methods to fill these.
/// </summary>
public static partial class CatgaGeneratedServiceRegistrations
{
    /// <summary>
    /// Registers all services marked with [CatgaService].
    /// Override this in consuming projects using Source Generator.
    /// </summary>
    public static IServiceCollection AddGeneratedServices(this IServiceCollection services)
    {
        // Default implementation does nothing
        // Source Generator will create a partial class with actual registrations
        return services;
    }
}

