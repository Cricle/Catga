using Catga.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.DependencyInjection;

/// <summary>
/// 托管服务扩展方法
/// </summary>
public static class CatgaHostingExtensions
{
    /// <summary>
    /// 添加 Catga 托管服务支持
    /// </summary>
    /// <param name="builder">Catga 服务构建器</param>
    /// <param name="configure">配置选项的委托</param>
    /// <returns>Catga 服务构建器，用于链式调用</returns>
    public static CatgaServiceBuilder AddHostedServices(
        this CatgaServiceBuilder builder,
        Action<HostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new HostingOptions();
        configure?.Invoke(options);
        
        // 验证配置
        options.Validate();
        
        // 注册配置选项
        builder.Services.AddSingleton(options);
        
        // 注册恢复服务
        if (options.EnableAutoRecovery)
        {
            builder.Services.AddSingleton(options.Recovery);
            builder.Services.AddHostedService<RecoveryHostedService>();
        }
        
        // 注册传输层托管服务
        if (options.EnableTransportHosting)
        {
            builder.Services.AddHostedService<TransportHostedService>();
        }
        
        // 注册 Outbox 处理器
        if (options.EnableOutboxProcessor)
        {
            builder.Services.AddSingleton(options.OutboxProcessor);
            builder.Services.AddHostedService<OutboxProcessorService>();
        }
        
        return builder;
    }
    
    /// <summary>
    /// 添加 Catga 健康检查
    /// </summary>
    /// <param name="builder">健康检查构建器</param>
    /// <returns>健康检查构建器，用于链式调用</returns>
    public static IHealthChecksBuilder AddCatgaHealthChecks(
        this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        // 注册传输层健康检查
        builder.AddCheck<TransportHealthCheck>(
            "catga_transport",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "catga", "transport", "ready" });
        
        // 注册持久化层健康检查
        builder.AddCheck<PersistenceHealthCheck>(
            "catga_persistence",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "catga", "persistence", "ready" });
        
        // 注册恢复服务健康检查
        builder.AddCheck<RecoveryHealthCheck>(
            "catga_recovery",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "catga", "recovery", "live" });
        
        return builder;
    }
    
    /// <summary>
    /// 添加 Catga 托管服务和健康检查（便捷方法）
    /// </summary>
    /// <param name="builder">Catga 服务构建器</param>
    /// <param name="configureHosting">配置托管选项的委托</param>
    /// <param name="configureHealthChecks">配置健康检查的委托</param>
    /// <returns>Catga 服务构建器，用于链式调用</returns>
    public static CatgaServiceBuilder AddHostingWithHealthChecks(
        this CatgaServiceBuilder builder,
        Action<HostingOptions>? configureHosting = null,
        Action<IHealthChecksBuilder>? configureHealthChecks = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        
        // 添加托管服务
        builder.AddHostedServices(configureHosting);
        
        // 添加健康检查
        var healthChecksBuilder = builder.Services.AddHealthChecks();
        healthChecksBuilder.AddCatgaHealthChecks();
        configureHealthChecks?.Invoke(healthChecksBuilder);
        
        return builder;
    }
}
