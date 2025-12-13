namespace OrderSystem.Api.Configuration;

/// <summary>
/// Catga infrastructure configuration options.
/// Loaded from appsettings.json section "Catga".
/// </summary>
public sealed class CatgaOptions
{
    public const string SectionName = "Catga";

    /// <summary>Transport type: InMemory, Redis, or Nats</summary>
    public string Transport { get; set; } = "InMemory";

    /// <summary>Persistence type: InMemory, Redis, or Nats</summary>
    public string Persistence { get; set; } = "InMemory";

    /// <summary>Redis connection string</summary>
    public string RedisConnection { get; set; } = "localhost:6379";

    /// <summary>NATS server URL</summary>
    public string NatsUrl { get; set; } = "nats://localhost:4222";

    /// <summary>SQLite connection string (when Persistence = SQLite)</summary>
    public string? SqliteConnection { get; set; } = "Data Source=orders.db";

    /// <summary>Enable cluster mode for distributed deployment</summary>
    public bool ClusterEnabled { get; set; } = false;

    /// <summary>Cluster node addresses (comma-separated)</summary>
    public string? ClusterNodes { get; set; }

    /// <summary>Enable development mode with verbose logging</summary>
    public bool DevelopmentMode { get; set; } = true;
}

/// <summary>
/// Flow DSL specific configuration options.
/// </summary>
public sealed class FlowDslOptions
{
    public const string SectionName = "FlowDsl";

    /// <summary>Auto-register flows from assembly</summary>
    public bool AutoRegisterFlows { get; set; } = true;

    /// <summary>Enable OpenTelemetry metrics</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Maximum retry attempts for failed steps</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Timeout for individual steps</summary>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
