using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Generated;

public static class GeneratedBootstrapRegistry
{
    private static readonly object _lock = new();
    private static readonly List<Action<IServiceCollection>> _serviceRegistrations = new();
    private static Func<Type, string>? _endpointConvention;

    // Called by source-generated ModuleInitializers at module load time
    public static void Register(Action<IServiceCollection> action)
    {
        if (action is null) return;
        lock (_lock)
        {
            _serviceRegistrations.Add(action);
        }
    }

    // Called by source-generated ModuleInitializer for endpoint naming
    public static void RegisterEndpointConvention(Func<Type, string> convention)
    {
        if (convention is null) return;
        lock (_lock)
        {
            _endpointConvention = convention;
        }
    }

    // Applied by AddCatga() to wire up generated registrations without reflection
    internal static void Apply(IServiceCollection services, Catga.Configuration.CatgaOptions options)
    {
        List<Action<IServiceCollection>> actions;
        Func<Type, string>? conv;
        lock (_lock)
        {
            // copy snapshot to avoid holding lock during user code
            actions = _serviceRegistrations.ToList();
            conv = _endpointConvention;
        }

        foreach (var action in actions)
        {
            try { action(services); }
            catch { /* ignore to avoid blocking startup */ }
        }

        if (conv is not null && options.EndpointNamingConvention is null)
        {
            options.EndpointNamingConvention = conv;
        }
    }
}
