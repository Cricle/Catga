namespace Catga.Configuration;

/// <summary>Catga configuration with sensible defaults (immutable record)</summary>
public record CatgaOptions
{
    public bool EnableLogging { get; init; } = true;
    public bool EnableTracing { get; init; } = true;
    public bool EnableRetry { get; init; } = true;
    public bool EnableValidation { get; init; } = true;
    public bool EnableIdempotency { get; init; } = true;

    public int MaxRetryAttempts { get; init; } = 3;
    public int RetryDelayMs { get; init; } = 100;
    public int TimeoutSeconds { get; init; } = 30;

    public int IdempotencyRetentionHours { get; init; } = 24;
    public int IdempotencyShardCount { get; init; } = 32;

    public bool EnableDeadLetterQueue { get; init; } = true;
    public int DeadLetterQueueMaxSize { get; init; } = 1000;

    public QualityOfService DefaultQoS { get; init; } = QualityOfService.AtLeastOnce;

    public CatgaOptions WithHighPerformance() => this with
    {
        IdempotencyShardCount = 64,
        EnableRetry = false,
        EnableValidation = false
    };

    public CatgaOptions Minimal() => this with
    {
        EnableLogging = false,
        EnableTracing = false,
        EnableIdempotency = false,
        EnableRetry = false,
        EnableValidation = false,
        EnableDeadLetterQueue = false
    };

    public CatgaOptions ForDevelopment() => this with
    {
        EnableLogging = true,
        EnableTracing = true,
        EnableIdempotency = false
    };
}
