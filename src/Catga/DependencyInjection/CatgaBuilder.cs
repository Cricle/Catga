using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Configuration;
using Catga.Handlers;
using Catga.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Catga fluent configuration builder
/// </summary>
public class CatgaBuilder
{
    private readonly IServiceCollection _services;
    private readonly CatgaOptions _options;

    /// <summary>
    /// Access to the service collection (for extension methods)
    /// </summary>
    public IServiceCollection Services => _services;

    public CatgaBuilder(IServiceCollection services, CatgaOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// Auto-scan and register all handlers in assembly
    /// WARNING: Uses reflection, not compatible with NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Assembly scanning uses reflection, not compatible with NativeAOT. Use manual registration in production.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Type scanning may require dynamic code generation, not compatible with NativeAOT")]
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
    /// Scan calling assembly (current executing assembly)
    /// WARNING: Uses reflection, not compatible with NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Assembly scanning uses reflection, not compatible with NativeAOT. Use manual registration in production.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Type scanning may require dynamic code generation, not compatible with NativeAOT")]
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
    /// Enable NATS distributed messaging
    /// </summary>
    public CatgaBuilder WithNats(string connectionString)
    {
        // Extension method support required
        return this;
    }

    /// <summary>
    /// Enable Redis state storage
    /// </summary>
    public CatgaBuilder WithRedis(string connectionString)
    {
        // Extension method support required
        return this;
    }

    /// <summary>
    /// Enable performance optimizations
    /// </summary>
    public CatgaBuilder WithPerformanceOptimization()
    {
        _options.EnableLogging = false; // Disable verbose logging in production
        _options.IdempotencyShardCount = 32; // Increase shard count
        return this;
    }

    /// <summary>
    /// Enable all reliability features
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
    /// Custom configuration
    /// </summary>
    public CatgaBuilder Configure(Action<CatgaOptions> configure)
    {
        configure(_options);
        return this;
    }
}

