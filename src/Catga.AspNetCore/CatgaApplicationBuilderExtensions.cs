using Catga.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Catga application builder extensions (CAP-like)</summary>
public static class CatgaApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCatga(this IApplicationBuilder app, Action<CatgaAspNetCoreOptions>? configure = null)
    {
        var options = new CatgaAspNetCoreOptions();
        configure?.Invoke(options);

        // TODO: Implement MapCatgaDiagnostics
        // if (options.EnableDashboard && app is WebApplication webApp)
        //     webApp.MapCatgaDiagnostics(options.DashboardPathPrefix);

        return app;
    }
}

