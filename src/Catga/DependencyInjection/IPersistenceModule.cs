using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Interface for persistence modules that can register their services.
/// Implements Open/Closed Principle - open for extension, closed for modification.
/// </summary>
public interface IPersistenceModule
{
    /// <summary>
    /// Register all services provided by this persistence module.
    /// </summary>
    void RegisterServices(IServiceCollection services);
}

/// <summary>
/// Extension methods for persistence module registration.
/// </summary>
public static class PersistenceModuleExtensions
{
    /// <summary>
    /// Add a persistence module to the service collection.
    /// </summary>
    public static IServiceCollection AddPersistenceModule<TModule>(
        this IServiceCollection services,
        Action<TModule>? configure = null)
        where TModule : IPersistenceModule, new()
    {
        var module = new TModule();
        configure?.Invoke(module);
        module.RegisterServices(services);
        return services;
    }

    /// <summary>
    /// Add a persistence module instance to the service collection.
    /// </summary>
    public static IServiceCollection AddPersistenceModule(
        this IServiceCollection services,
        IPersistenceModule module)
    {
        module.RegisterServices(services);
        return services;
    }
}
