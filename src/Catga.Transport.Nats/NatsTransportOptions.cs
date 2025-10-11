namespace Catga.Transport.Nats;

/// <summary>
/// NATS transport configuration options
/// </summary>
public class NatsTransportOptions
{
    public string Url { get; set; } = "nats://localhost:4222";

    public string SubjectPrefix { get; set; } = "catga.";

    public int ConnectTimeout { get; set; } = 5;

    public int RequestTimeout { get; set; } = 30;

    public bool EnableJetStream { get; set; } = false;

    public string? StreamName { get; set; }
}

