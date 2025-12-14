namespace Catga.Flow.Dsl;

/// <summary>
/// DSL Flow configuration options.
/// </summary>
public class DslFlowOptions
{
    /// <summary>Key prefix for storage.</summary>
    public string KeyPrefix { get; set; } = "flow";

    /// <summary>Default timeout for steps.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Default retry count.</summary>
    public int DefaultRetries { get; set; } = 0;

    /// <summary>Enable distributed tracing.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Enable metrics collection.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Node ID for distributed scenarios.</summary>
    public string? NodeId { get; set; }

    /// <summary>Heartbeat interval.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Claim timeout for abandoned flows.</summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
