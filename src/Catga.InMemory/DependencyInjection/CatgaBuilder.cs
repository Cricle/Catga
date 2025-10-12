using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Configuration;
using Catga.Handlers;
using Catga.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>Catga fluent configuration builder</summary>
public class CatgaBuilder
{
    private readonly IServiceCollection _services;
    private readonly CatgaOptions _options;

    public IServiceCollection Services => _services;

    public CatgaBuilder(IServiceCollection services, CatgaOptions options)
    {
        _services = services;
        _options = options;
    }

    [RequiresUnreferencedCode("Assembly scanning uses reflection, not compatible with NativeAOT")]
    [RequiresDynamicCode("Type scanning may require dynamic code generation")]
    public CatgaBuilder ScanHandlers(Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && (
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IEventHandler<>))));

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && (
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IEventHandler<>)));

            foreach (var @interface in interfaces)
                _services.AddTransient(@interface, handlerType);
        }
        return this;
    }

    [RequiresUnreferencedCode("Assembly scanning uses reflection, not compatible with NativeAOT")]
    [RequiresDynamicCode("Type scanning may require dynamic code generation")]
    public CatgaBuilder ScanCurrentAssembly() => ScanHandlers(Assembly.GetCallingAssembly());

    public CatgaBuilder WithOutbox(Action<OutboxOptions>? configure = null)
    {
        _services.AddOutbox(configure);
        return this;
    }

    public CatgaBuilder WithInbox(Action<InboxOptions>? configure = null)
    {
        _services.AddInbox(configure);
        return this;
    }

    public CatgaBuilder WithNats(string connectionString) => this;

    public CatgaBuilder WithRedis(string connectionString) => this;

    public CatgaBuilder WithPerformanceOptimization()
    {
        _options.EnableLogging = false;
        _options.IdempotencyShardCount = 32;
        return this;
    }

    public CatgaBuilder WithReliability()
    {
        _options.EnableRetry = true;
        _options.EnableDeadLetterQueue = true;
        _options.EnableIdempotency = true;
        return this;
    }

    public CatgaBuilder Configure(Action<CatgaOptions> configure)
    {
        configure(_options);
        return this;
    }
}

