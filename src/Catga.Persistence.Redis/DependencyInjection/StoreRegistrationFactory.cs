using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Generic store registration factory (Open/Closed Principle).
/// Allows extensible registration of different store types without modifying existing code.
/// </summary>
public interface IStoreRegistration
{
    /// <summary>Register store with default configuration.</summary>
    void Register(IServiceCollection services);

    /// <summary>Register store with custom configuration.</summary>
    void Register(IServiceCollection services, Action<object> configure);
}

/// <summary>
/// Base class for store registrations (DRY principle).
/// </summary>
public abstract class StoreRegistrationBase<TInterface, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation> : IStoreRegistration
    where TInterface : class
    where TImplementation : class, TInterface
{
    /// <summary>Register store with default configuration.</summary>
    public virtual void Register(IServiceCollection services)
    {
        services.TryAddSingleton<TInterface, TImplementation>();
    }

    /// <summary>Register store with custom configuration.</summary>
    public virtual void Register(IServiceCollection services, Action<object> configure)
    {
        Register(services);
    }
}

/// <summary>
/// Factory for managing store registrations (Open/Closed Principle).
/// </summary>
public sealed class StoreRegistrationFactory
{
    private readonly Dictionary<string, IStoreRegistration> _registrations = new();

    /// <summary>Register a store registration.</summary>
    public void Register(string name, IStoreRegistration registration)
    {
        _registrations[name] = registration;
    }

    /// <summary>Get a store registration.</summary>
    public IStoreRegistration? Get(string name)
    {
        return _registrations.TryGetValue(name, out var registration) ? registration : null;
    }

    /// <summary>Apply all registrations to service collection.</summary>
    public void ApplyAll(IServiceCollection services)
    {
        foreach (var registration in _registrations.Values)
        {
            registration.Register(services);
        }
    }
}
