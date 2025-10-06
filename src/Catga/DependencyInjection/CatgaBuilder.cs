using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Configuration;
using Catga.Handlers;
using Catga.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// 🚀 Catga 流式配置构建器 - 让配置更简单
/// </summary>
public class CatgaBuilder
{
    private readonly IServiceCollection _services;
    private readonly CatgaOptions _options;

    public CatgaBuilder(IServiceCollection services, CatgaOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// 🔍 自动扫描并注册指定程序集中的所有 Handlers
    /// ⚠️ 警告: 使用反射，不兼容 NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("程序集扫描使用反射，不兼容 NativeAOT。生产环境请使用手动注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("类型扫描可能需要动态代码生成，不兼容 NativeAOT")]
    public CatgaBuilder ScanHandlers(Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && (
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                )));

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && (
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                ));

            foreach (var @interface in interfaces)
            {
                _services.AddTransient(@interface, handlerType);
            }
        }

        return this;
    }

    /// <summary>
    /// 🔍 扫描调用程序集（当前执行程序集）
    /// ⚠️ 警告: 使用反射，不兼容 NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("程序集扫描使用反射，不兼容 NativeAOT。生产环境请使用手动注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("类型扫描可能需要动态代码生成，不兼容 NativeAOT")]
    public CatgaBuilder ScanCurrentAssembly()
    {
        return ScanHandlers(Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Enable Outbox pattern (reliable message delivery)
    /// </summary>
    [RequiresUnreferencedCode("Outbox requires serialization. Use AOT-friendly serializer in production")]
    [RequiresDynamicCode("Outbox requires serialization. Use AOT-friendly serializer in production")]
    public CatgaBuilder WithOutbox(Action<OutboxOptions>? configure = null)
    {
        _services.AddOutbox(configure);
        return this;
    }

    /// <summary>
    /// Enable Inbox pattern (idempotent processing)
    /// </summary>
    [RequiresUnreferencedCode("Inbox requires serialization. Use AOT-friendly serializer in production")]
    [RequiresDynamicCode("Inbox requires serialization. Use AOT-friendly serializer in production")]
    public CatgaBuilder WithInbox(Action<InboxOptions>? configure = null)
    {
        _services.AddInbox(configure);
        return this;
    }

    /// <summary>
    /// 🌐 启用 NATS 分布式消息
    /// </summary>
    public CatgaBuilder WithNats(string connectionString)
    {
        // 这里需要扩展方法支持，暂时保留接口
        return this;
    }

    /// <summary>
    /// 🗄️ 启用 Redis 状态存储
    /// </summary>
    public CatgaBuilder WithRedis(string connectionString)
    {
        // 这里需要扩展方法支持，暂时保留接口
        return this;
    }

    /// <summary>
    /// ⚡ 启用性能优化
    /// </summary>
    public CatgaBuilder WithPerformanceOptimization()
    {
        _options.EnableLogging = false; // 生产环境关闭详细日志
        _options.IdempotencyShardCount = 32; // 增加分片数
        return this;
    }

    /// <summary>
    /// 🛡️ 启用全部可靠性特性
    /// </summary>
    public CatgaBuilder WithReliability()
    {
        _options.EnableCircuitBreaker = true;
        _options.EnableRetry = true;
        _options.EnableDeadLetterQueue = true;
        _options.EnableIdempotency = true;
        return this;
    }

    /// <summary>
    /// 🔧 自定义配置
    /// </summary>
    public CatgaBuilder Configure(Action<CatgaOptions> configure)
    {
        configure(_options);
        return this;
    }
}

