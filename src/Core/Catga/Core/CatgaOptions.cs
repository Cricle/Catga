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
    public int IdempotencyRetentionHours { get; set; } = 24;
    public int IdempotencyShardCount { get; set; } = 32;

    // === Dead Letter Queue ===
    public bool EnableDeadLetterQueue { get; set; } = true;
    public int DeadLetterQueueMaxSize { get; set; } = 1000;

    // === Quality of Service ===
    /// <summary>
    /// 默认消息服务质量等级（QoS）
    /// - AtMostOnce (0): 最快，不保证送达
    /// - AtLeastOnce (1): 默认，保证送达但可能重复
    /// - ExactlyOnce (2): 最慢，保证送达且不重复
    /// </summary>
    public QualityOfService DefaultQoS { get; set; } = QualityOfService.AtLeastOnce;

    // === Quick Presets ===

    /// <summary>
    /// High performance: 64 shards, no retry/validation
    /// </summary>
    public CatgaOptions WithHighPerformance()
    {
        IdempotencyShardCount = 64;
        EnableRetry = false;
        EnableValidation = false;
        return this;
    }

    /// <summary>
    /// Minimal configuration: Maximum performance, remove all non-essential features
    /// </summary>
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

    /// <summary>
    /// Development: all logging, no idempotency
    /// </summary>
    public CatgaOptions ForDevelopment()
    {
        EnableLogging = true;
        EnableTracing = true;
        EnableIdempotency = false;
        return this;
    }

}
