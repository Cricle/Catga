using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Catga.Configuration;

namespace Catga.Generated;

public static class GeneratedBootstrapRegistry
{
    private static readonly object _gate = new();
    private static readonly List<Action<IServiceCollection>> _registrations = new();
    private static Func<Type, string>? _endpointConvention;

    public static void Register(Action<IServiceCollection> registration)
    {
        if (registration is null) return;
        lock (_gate) _registrations.Add(registration);
    }

    public static void RegisterEndpointConvention(Func<Type, string> convention)
    {
        if (convention is null) return;
        _endpointConvention = convention;
    }

    internal static void Apply(IServiceCollection services, CatgaOptions options)
    {
        Action<IServiceCollection>[] regs;
        lock (_gate) regs = _registrations.ToArray();
        for (int i = 0; i < regs.Length; i++)
        {
            regs[i](services);
        }

        if (options.EndpointNamingConvention is null && _endpointConvention is not null)
        {
            options.EndpointNamingConvention = _endpointConvention;
        }
    }
}
