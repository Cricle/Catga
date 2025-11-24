using System;
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
}

