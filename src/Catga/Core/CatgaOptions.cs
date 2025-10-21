namespace Catga.Configuration;

/// <summary>Catga configuration with sensible defaults</summary>
public class CatgaOptions
{
    public bool EnableLogging { get; set; } = true;
    public bool EnableTracing { get; set; } = true;
    public bool EnableRetry { get; set; } = true;
    public bool EnableValidation { get; set; } = true;
    public bool EnableIdempotency { get; set; } = true;

    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 30;

    public int IdempotencyRetentionHours { get; set; } = 24;
    public int IdempotencyShardCount { get; set; } = 32;

    public bool EnableDeadLetterQueue { get; set; } = true;
    public int DeadLetterQueueMaxSize { get; set; } = 1000;

    public QualityOfService DefaultQoS { get; set; } = QualityOfService.AtLeastOnce;

    /// <summary>Circuit breaker: consecutive failure threshold before opening (default: 5)</summary>
    public int? CircuitBreakerThreshold { get; set; }

    /// <summary>Circuit breaker: duration to keep circuit open (default: 30 seconds)</summary>
    public TimeSpan? CircuitBreakerDuration { get; set; }

    /// <summary>Event handler maximum concurrency (default: null, unlimited). Set to limit concurrent event handlers.</summary>
    public int? MaxEventHandlerConcurrency { get; set; }

    public CatgaOptions WithHighPerformance()
    {
        IdempotencyShardCount = 64;
        EnableRetry = false;
        EnableValidation = false;
        return this;
    }

    public CatgaOptions Minimal()
    {
        EnableLogging = false;
        EnableTracing = false;
        EnableIdempotency = false;
        EnableRetry = false;
        EnableValidation = false;
        EnableDeadLetterQueue = false;
        return this;
    }

    public CatgaOptions ForDevelopment()
    {
        EnableLogging = true;
        EnableTracing = true;
        EnableIdempotency = false;
        return this;
    }
}
