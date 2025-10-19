using Catga.Configuration;
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

