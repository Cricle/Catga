namespace Catga.Transport.Nats;

/// <summary>NATS transport options (immutable record)</summary>
public record NatsTransportOptions
{
    public string SubjectPrefix { get; init; } = "catga.";

    /// <summary>
    /// Optional subject naming convention. If provided, the final subject will be
    /// SubjectPrefix (if any) + computed name. Keep it simple and consistent across transports.
    /// </summary>
    public Func<Type, string>? Naming { get; init; }

    /// <summary>
    /// Optional auto-batching configuration. When null, auto-batching is disabled by default.
    /// </summary>
    public BatchTransportOptions? Batch { get; init; }

    /// <summary>
    /// Upper bound on pending queue length when auto-batching is enabled. Oldest items will be dropped when exceeded.
    /// </summary>
    public int MaxQueueLength { get; init; } = 10000;
}

