using Catga.Configuration;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
public class CatgaServiceBuilder(IServiceCollection services, CatgaOptions options)
{
    /// <summary>
    /// Get the underlying service collection
    /// </summary>
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Get the Catga options
    /// </summary>
    public CatgaOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Configure Catga options
    /// </summary>
    public CatgaServiceBuilder Configure(Action<CatgaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options);
        return this;
    }

    /// <summary>
    /// Configure for development environment (verbose logging, no idempotency)
    /// </summary>
    public CatgaServiceBuilder ForDevelopment()
    {
        Options.ForDevelopment();
        return this;
    }

    /// <summary>
    /// Configure for production environment (all features enabled)
    /// </summary>
    public CatgaServiceBuilder ForProduction()
    {
        Options.EnableLogging = true;
        Options.EnableTracing = true;
        Options.EnableIdempotency = true;
        Options.EnableRetry = true;
        Options.EnableValidation = true;
        Options.EnableDeadLetterQueue = true;
        return this;
    }

    /// <summary>
    /// Configure for high-performance scenarios (minimal overhead)
    /// </summary>
    public CatgaServiceBuilder ForHighPerformance()
    {
        Options.WithHighPerformance();
        return this;
    }

    /// <summary>
    /// Configure with minimal features (fastest startup, lowest memory)
    /// </summary>
    public CatgaServiceBuilder Minimal()
    {
        Options.Minimal();
        return this;
    }

    /// <summary>
    /// Enable logging
    /// </summary>
    public CatgaServiceBuilder WithLogging(bool enabled = true)
    {
        Options.EnableLogging = enabled;
        return this;
    }

    /// <summary>
    /// Enable distributed tracing (OpenTelemetry for Jaeger/Zipkin with rich message details)
    /// </summary>
    public CatgaServiceBuilder WithTracing(bool enabled = true)
    {
        Options.EnableTracing = enabled;

        if (enabled)
        {
            // Register distributed tracing behavior for rich trace data in Jaeger
            Services.AddScoped(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof(Catga.Pipeline.Behaviors.DistributedTracingBehavior<,>));
            ObservabilityHooks.Enable();
        }

        return this;
    }

    /// <summary>
    /// Enable retry logic
    /// </summary>
    public CatgaServiceBuilder WithRetry(bool enabled = true, int maxAttempts = 3)
    {
        Options.EnableRetry = enabled;
        Options.MaxRetryAttempts = maxAttempts;
        return this;
    }

    /// <summary>
    /// Enable idempotency
    /// </summary>
    public CatgaServiceBuilder WithIdempotency(bool enabled = true, int retentionHours = 24)
    {
        Options.EnableIdempotency = enabled;
        Options.IdempotencyRetentionHours = retentionHours;
        return this;
    }

    /// <summary>
    /// Enable validation
    /// </summary>
    public CatgaServiceBuilder WithValidation(bool enabled = true)
    {
        Options.EnableValidation = enabled;
        return this;
    }

    /// <summary>
    /// Enable dead letter queue
    /// </summary>
    public CatgaServiceBuilder WithDeadLetterQueue(bool enabled = true, int maxSize = 1000)
    {
        Options.EnableDeadLetterQueue = enabled;
        Options.DeadLetterQueueMaxSize = maxSize;
        return this;
    }

    /// <summary>
    /// Configure distributed ID generator with specific worker ID for cluster deployment
    /// </summary>
    /// <param name="workerId">Worker ID (0-255 for default Snowflake layout)</param>
    /// <remarks>
    /// Essential for distributed/cluster scenarios where each node needs a unique WorkerId.
    /// Example: Node 1 uses WorkerId=1, Node 2 uses WorkerId=2, etc.
    /// </remarks>
    public CatgaServiceBuilder UseWorkerId(int workerId)
    {
        if (workerId < 0 || workerId > 255)
            throw new ArgumentOutOfRangeException(nameof(workerId), "WorkerId must be between 0 and 255");

        // Replace the default IDistributedIdGenerator with a configured one
        Services.AddSingleton<Catga.DistributedId.IDistributedIdGenerator>(sp =>
            new Catga.DistributedId.SnowflakeIdGenerator(workerId));

        return this;
    }

    /// <summary>
    /// Configure distributed ID generator with worker ID from environment variable
    /// </summary>
    /// <param name="envVarName">Environment variable name (default: CATGA_WORKER_ID)</param>
    /// <remarks>
    /// Recommended for containerized deployments (Docker, Kubernetes).
    /// Set CATGA_WORKER_ID environment variable for each node/pod.
    /// </remarks>
    public CatgaServiceBuilder UseWorkerIdFromEnvironment(string envVarName = "CATGA_WORKER_ID")
    {
        var workerId = GetWorkerIdFromEnvironment(envVarName);
        return UseWorkerId(workerId);
    }

    public CatgaServiceBuilder UseInbox()
    {
        Services.AddScoped(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof(Catga.Pipeline.Behaviors.InboxBehavior<,>));
        return this;
    }

    public CatgaServiceBuilder UseOutbox()
    {
        Services.AddScoped(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof(Catga.Pipeline.Behaviors.OutboxBehavior<,>));
        return this;
    }

    public CatgaServiceBuilder UseDeadLetterQueue()
    {
        // No-op: concrete DeadLetterBehavior not provided; keep API for compatibility
        return this;
    }

    /// <summary>
    /// Enable automatic compensation for commands marked with [Compensatable] or [OnFailure].
    /// On failure, compensation events/commands are automatically published.
    /// </summary>
    public CatgaServiceBuilder UseAutoCompensation()
    {
        Services.AddScoped(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof(Catga.Pipeline.Behaviors.CompensationBehavior<,>));
        return this;
    }

    /// <summary>
    /// Enable Polly-based resilience (retry/timeout/circuit/bulkhead) with multi-TFM support
    /// </summary>
    public CatgaServiceBuilder UseResilience(Action<CatgaResilienceOptions>? configure = null)
    {
        Services.AddCatgaResilience(configure);
        return this;
    }

    /// <summary>
    /// Enable mediator-level auto-batching (default off). When enabled, requests of the same type are queued and flushed by size/time thresholds.
    /// Execution is wrapped by the mediator resilience pipeline.
    /// </summary>
    public CatgaServiceBuilder UseMediatorAutoBatching(Action<Catga.Pipeline.MediatorBatchOptions>? configure = null)
    {
        var options = new Catga.Pipeline.MediatorBatchOptions();
        configure?.Invoke(options);

        // Expose options to behaviors via DI
        Services.AddSingleton(options);

        // Ensure default provider exists for per-type overrides fallback
        Services.TryAddSingleton<Catga.Pipeline.IMediatorBatchOptionsProvider, Catga.Pipeline.DefaultMediatorBatchOptionsProvider>();

        if (options.EnableAutoBatching)
        {
            Services.TryAddSingleton<CatgaResilienceOptions>(_ => new CatgaResilienceOptions());
            Services.TryAddSingleton<IResiliencePipelineProvider>(sp => new DefaultResiliencePipelineProvider(sp.GetRequiredService<CatgaResilienceOptions>()));

            Services.AddScoped(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof(Catga.Pipeline.Behaviors.AutoBatchingBehavior<,>));
        }

        return this;
    }

    private static int GetWorkerIdFromEnvironment(string envVarName)
    {
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out var workerId))
        {
            // Validate worker ID is within valid range (0-255 for default 44-8-11 Snowflake layout)
            if (workerId >= 0 && workerId <= 255)
            {
                Console.WriteLine($"[Catga] Using WorkerId from {envVarName}: {workerId}");
                return workerId;
            }
        }

        // Generate a random worker ID (0-255 for default 8-bit worker ID)
        // WARNING: Random WorkerId is NOT recommended for production clusters!
        var randomWorkerId = Random.Shared.Next(0, 256);
        Console.WriteLine($"[Catga] ⚠️ No valid {envVarName} found, using random WorkerId: {randomWorkerId} (NOT recommended for production!)");
        return randomWorkerId;
    }
}
