using System;
using Catga.Transport;
namespace Catga.Transport.Nats;

/// <summary>NATS transport options (immutable record)</summary>
public record NatsTransportOptions
{
    public string Url { get; init; } = "nats://localhost:4222";
    public string SubjectPrefix { get; init; } = "catga.";
    public int ConnectTimeout { get; init; } = 5;
    public int RequestTimeout { get; init; } = 30;
    public bool EnableJetStream { get; init; } = false;
    public string? StreamName { get; init; }

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

