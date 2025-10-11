using System.Diagnostics.CodeAnalysis;
using Catga.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Catga application builder extensions
/// Similar to app.UseCap() in CAP
/// </summary>
public static class CatgaApplicationBuilderExtensions
{
    /// <summary>
    /// Use Catga ASP.NET Core features
    /// Usage: app.UseCatga()
    /// </summary>
    [RequiresUnreferencedCode("Catga ASP.NET Core integration may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("Catga ASP.NET Core integration uses reflection for request binding.")]
    public static IApplicationBuilder UseCatga(
        this IApplicationBuilder app,
        Action<CatgaAspNetCoreOptions>? configure = null)
    {
        var options = new CatgaAspNetCoreOptions();
        configure?.Invoke(options);

        // Auto map diagnostics if enabled
        if (options.EnableDashboard && app is WebApplication webApp)
        {
            webApp.MapCatgaDiagnostics(options.DashboardPathPrefix);
        }

        return app;
    }
}

