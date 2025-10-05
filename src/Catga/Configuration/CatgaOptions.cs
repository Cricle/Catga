namespace Catga.Configuration;

/// <summary>
/// Simple configuration for Catga with sensible defaults
/// </summary>
public class CatgaOptions
{
    // === Pipeline Behaviors ===
    public bool EnableLogging { get; set; } = true;
    public bool EnableTracing { get; set; } = true;
    public bool EnableRetry { get; set; } = true;
    public bool EnableValidation { get; set; } = true;
    public bool EnableIdempotency { get; set; } = true;

    // === Retry Settings ===
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 30;

    // === Performance ===
    public int MaxConcurrentRequests { get; set; } = 1000;
    public int IdempotencyRetentionHours { get; set; } = 24;
    public int IdempotencyShardCount { get; set; } = 32;

    // === Resilience (Optional) ===
    public bool EnableCircuitBreaker { get; set; } = false;
    public int CircuitBreakerFailureThreshold { get; set; } = 10;
    public int CircuitBreakerResetTimeoutSeconds { get; set; } = 60;

    public bool EnableRateLimiting { get; set; } = false;
    public int RateLimitRequestsPerSecond { get; set; } = 1000;
    public int RateLimitBurstCapacity { get; set; } = 2000;

    // === Dead Letter Queue ===
    public bool EnableDeadLetterQueue { get; set; } = true;
    public int DeadLetterQueueMaxSize { get; set; } = 1000;

    // === Quick Presets ===

    /// <summary>
    /// High performance: 5000 concurrent, 64 shards, no retry/validation
    /// </summary>
    public CatgaOptions WithHighPerformance()
    {
        MaxConcurrentRequests = 5000;
        IdempotencyShardCount = 64;
        EnableRetry = false;
        EnableValidation = false;
        return this;
    }

    /// <summary>
    /// Full resilience: circuit breaker + rate limiting
    /// </summary>
    public CatgaOptions WithResilience()
    {
        EnableCircuitBreaker = true;
        EnableRateLimiting = true;
        return this;
    }

    /// <summary>
    /// 极简配置：最大性能，移除所有非必要功能
    /// </summary>
    public CatgaOptions Minimal()
    {
        EnableLogging = false;
        EnableTracing = false;
        EnableIdempotency = false;
        EnableRetry = false;
        EnableValidation = false;
        EnableCircuitBreaker = false;
        EnableRateLimiting = false;
        EnableDeadLetterQueue = false;
        MaxConcurrentRequests = 0; // 无限制
        return this;
    }

    /// <summary>
    /// Development: all logging, no rate limiting, no idempotency
    /// </summary>
    public CatgaOptions ForDevelopment()
    {
        EnableLogging = true;
        EnableTracing = true;
        EnableIdempotency = false;
        EnableCircuitBreaker = false;
        EnableRateLimiting = false;
        MaxConcurrentRequests = 0; // Unlimited
        return this;
    }
}
