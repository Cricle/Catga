using Catga.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Catga application builder extensions (CAP-like)</summary>
public static class CatgaApplicationBuilderExtensions
{
    /// <summary>
    /// 启用 Catga 集成，包括诊断仪表板（如果启用）
    /// </summary>
    /// <param name="app">应用程序构建器</param>
    /// <param name="configure">配置选项</param>
    /// <returns>应用程序构建器</returns>
    public static IApplicationBuilder UseCatga(this IApplicationBuilder app, Action<CatgaAspNetCoreOptions>? configure = null)
    {
        var options = new CatgaAspNetCoreOptions();
        configure?.Invoke(options);

        // 映射诊断仪表板端点
        if (options.EnableDashboard && app is IEndpointRouteBuilder endpoints)
        {
            endpoints.MapCatgaDiagnostics(options.DashboardPathPrefix);
        }

        return app;
    }
}

