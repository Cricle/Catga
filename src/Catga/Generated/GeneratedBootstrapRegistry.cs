using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Generated;

/// <summary>
/// Minimal bootstrap registry to satisfy source-generated references.
/// Methods are no-ops; generators may call them via ModuleInitializer.
/// Thread-safe and lock-free for high-performance scenarios.
/// </summary>
public static class GeneratedBootstrapRegistry
{
    private static readonly ConcurrentBag<Action<IServiceCollection>> _registrations = new();
    private static volatile Func<Type, string>? _endpointConvention;
    private static volatile bool _applied;

    /// <summary>
    /// Registers a service configuration action to be applied during DI setup.
    /// </summary>
    /// <param name="registration">The registration action to execute.</param>
    public static void Register(Action<IServiceCollection> registration)
    {
        if (registration is null) return;
        _registrations.Add(registration);
    }

    /// <summary>
    /// Registers a convention for generating endpoint routes from types.
    /// </summary>
    /// <param name="convention">The convention function.</param>
    public static void RegisterEndpointConvention(Func<Type, string> convention)
    {
        _endpointConvention = convention;
    }

    /// <summary>
    /// Applies all registered service configurations to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static void Apply(IServiceCollection services)
    {
        if (services is null) return;
        
        foreach (var registration in _registrations)
        {
            registration(services);
        }
        
        _applied = true;
    }

    /// <summary>
    /// Gets the registered endpoint convention, if any.
    /// </summary>
    public static Func<Type, string>? EndpointConvention => _endpointConvention;

    /// <summary>
    /// Gets whether Apply has been called at least once.
    /// </summary>
    public static bool HasApplied => _applied;

    /// <summary>
    /// Gets the count of registered actions.
    /// </summary>
    public static int RegistrationCount => _registrations.Count;
}
