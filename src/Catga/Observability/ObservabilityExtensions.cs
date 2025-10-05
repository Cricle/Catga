using Catga.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Catga 可观测性扩展方法
/// </summary>
public static class CatgaObservabilityExtensions
{
    /// <summary>
    /// 添加 Catga 完整可观测性支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealth">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCatgaObservability(
        this IServiceCollection services,
        Action<CatgaHealthCheckOptions>? configureHealth = null)
    {
        // 添加健康检查
        services.AddCatgaHealthChecks(configureHealth);

        // Metrics 和 Tracing 是静态的，已经自动启用
        // 只需要确保 OpenTelemetry 配置正确

        return services;
    }

    /// <summary>
    /// 添加 Catga 健康检查
    /// </summary>
    public static IServiceCollection AddCatgaHealthChecks(
        this IServiceCollection services,
        Action<CatgaHealthCheckOptions>? configure = null)
    {
        var options = new CatgaHealthCheckOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        services.AddHealthChecks()
            .AddCheck<CatgaHealthCheck>(
                "catga",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "catga", "framework", "ready" },
                timeout: TimeSpan.FromSeconds(options.TimeoutSeconds));

        return services;
    }

    /// <summary>
    /// 配置 OpenTelemetry 以集成 Catga
    /// </summary>
    /// <remarks>
    /// 使用方式:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing.AddCatgaInstrumentation())
    ///     .WithMetrics(metrics => metrics.AddCatgaInstrumentation());
    /// </code>
    /// </remarks>
    public static IServiceCollection AddCatgaOpenTelemetry(
        this IServiceCollection services)
    {
        // 注册 OpenTelemetry 源
        // 实际的 OpenTelemetry 配置由用户在 Program.cs 中完成
        // 这里只是占位，说明如何集成

        return services;
    }
}

/// <summary>
/// OpenTelemetry 集成扩展（用于外部 OpenTelemetry 配置）
/// </summary>
public static class CatgaOpenTelemetryExtensions
{
    /// <summary>
    /// 添加 Catga 追踪仪器
    /// </summary>
    public static object AddCatgaInstrumentation(this object builder)
    {
        // 假设 builder 是 TracerProviderBuilder
        // builder.AddSource("Catga");
        return builder;
    }

    /// <summary>
    /// 添加 Catga 指标仪器
    /// </summary>
    public static object AddCatgaMetrics(this object builder)
    {
        // 假设 builder 是 MeterProviderBuilder
        // builder.AddMeter("Catga");
        return builder;
    }
}

