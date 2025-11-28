using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Generated;

/// <summary>
/// Minimal bootstrap registry to satisfy source-generated references.
/// Methods are no-ops; generators may call them via ModuleInitializer.
/// </summary>
public static class GeneratedBootstrapRegistry
{
    private static readonly object _sync = new();
    private static readonly List<Action<IServiceCollection>> _registrations = new();
    private static Func<Type, string>? _endpointConvention;

    public static void Register(Action<IServiceCollection> registration)
    {
        if (registration is null) return;
        lock (_sync) _registrations.Add(registration);
    }

    public static void RegisterEndpointConvention(Func<Type, string> convention)
    {
        lock (_sync) _endpointConvention = convention;
    }

    public static void Apply(IServiceCollection services)
    {
        if (services is null) return;
        Action<IServiceCollection>[] regs;
        lock (_sync) regs = _registrations.ToArray();
        foreach (var r in regs) r(services);
    }

    public static Func<Type, string>? EndpointConvention
    {
        get { lock (_sync) return _endpointConvention; }
    }
}
