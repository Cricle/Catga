using System;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Generated;

/// <summary>
/// Minimal bootstrap registry to satisfy source-generated references.
/// Methods are no-ops; generators may call them via ModuleInitializer.
/// </summary>
public static class GeneratedBootstrapRegistry
{
    public static void Register(Action<IServiceCollection> registration) { }
    public static void RegisterEndpointConvention(Func<Type, string> convention) { }
}
