using NATS.Client.JetStream.Models;

namespace Catga.Persistence;

/// <summary>
/// NATS JetStream store options. Only Catga-specific settings.
/// For stream configuration, use StreamConfigFactory or provide StreamConfig directly.
/// </summary>
public class NatsJSStoreOptions
{
    /// <summary>Stream name prefix (default: "CATGA").</summary>
    public string StreamName { get; set; } = "CATGA";

    /// <summary>Optional custom StreamConfig. If null, uses defaults.</summary>
    public StreamConfig? CustomStreamConfig { get; set; }

    /// <summary>Create default StreamConfig for a stream.</summary>
    public StreamConfig CreateDefaultStreamConfig(string streamName, string[] subjects)
    {
        return CustomStreamConfig ?? new StreamConfig(streamName, subjects)
        {
            Retention = StreamConfigRetention.Limits,
            Storage = StreamConfigStorage.File,
            MaxAge = TimeSpan.FromDays(7),
            Discard = StreamConfigDiscard.Old
        };
    }
}

