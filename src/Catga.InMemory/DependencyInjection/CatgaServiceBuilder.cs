using Catga.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Fluent builder for configuring Catga services
/// </summary>
/// <remarks>
/// Provides a clean, intuitive API for Catga configuration:
/// <code>
/// services.AddCatga()
///     .UseMemoryPack()
///     .AddNatsTransport()
///     .AddRedisCache()
///     .ForProduction();
/// </code>
/// </remarks>
public class CatgaServiceBuilder
{
    private readonly IServiceCollection _services;
    private readonly CatgaOptions _options;

    /// <summary>
    /// Get the underlying service collection
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Get the Catga options
    /// </summary>
    public CatgaOptions Options => _options;

    internal CatgaServiceBuilder(IServiceCollection services, CatgaOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// Configure Catga options
    /// </summary>
    public CatgaServiceBuilder Configure(Action<CatgaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options);
        return this;
    }

    /// <summary>
    /// Configure for development environment (verbose logging, no idempotency)
    /// </summary>
    public CatgaServiceBuilder ForDevelopment()
    {
        _options.ForDevelopment();
        return this;
    }

    /// <summary>
    /// Configure for production environment (all features enabled)
    /// </summary>
    public CatgaServiceBuilder ForProduction()
    {
        _options.EnableLogging = true;
        _options.EnableTracing = true;
        _options.EnableIdempotency = true;
        _options.EnableRetry = true;
        _options.EnableValidation = true;
        _options.EnableDeadLetterQueue = true;
        return this;
    }

    /// <summary>
    /// Configure for high-performance scenarios (minimal overhead)
    /// </summary>
    public CatgaServiceBuilder ForHighPerformance()
    {
        _options.WithHighPerformance();
        return this;
    }

    /// <summary>
    /// Configure with minimal features (fastest startup, lowest memory)
    /// </summary>
    public CatgaServiceBuilder Minimal()
    {
        _options.Minimal();
        return this;
    }

    /// <summary>
    /// Enable logging
    /// </summary>
    public CatgaServiceBuilder WithLogging(bool enabled = true)
    {
        _options.EnableLogging = enabled;
        return this;
    }

    /// <summary>
    /// Enable distributed tracing
    /// </summary>
    public CatgaServiceBuilder WithTracing(bool enabled = true)
    {
        _options.EnableTracing = enabled;
        return this;
    }

    /// <summary>
    /// Enable retry logic
    /// </summary>
    public CatgaServiceBuilder WithRetry(bool enabled = true, int maxAttempts = 3)
    {
        _options.EnableRetry = enabled;
        _options.MaxRetryAttempts = maxAttempts;
        return this;
    }

    /// <summary>
    /// Enable idempotency
    /// </summary>
    public CatgaServiceBuilder WithIdempotency(bool enabled = true, int retentionHours = 24)
    {
        _options.EnableIdempotency = enabled;
        _options.IdempotencyRetentionHours = retentionHours;
        return this;
    }

    /// <summary>
    /// Enable validation
    /// </summary>
    public CatgaServiceBuilder WithValidation(bool enabled = true)
    {
        _options.EnableValidation = enabled;
        return this;
    }

    /// <summary>
    /// Enable dead letter queue
    /// </summary>
    public CatgaServiceBuilder WithDeadLetterQueue(bool enabled = true, int maxSize = 1000)
    {
        _options.EnableDeadLetterQueue = enabled;
        _options.DeadLetterQueueMaxSize = maxSize;
        return this;
    }
}

