using System.Diagnostics.CodeAnalysis;

namespace Catga.Messaging;

/// <summary>
/// Competing consumer interface. Multiple instances compete to process messages
/// from the same queue/stream — only one processes each message.
/// Equivalent to MassTransit's competing consumer pattern.
/// </summary>
public interface ICompetingConsumer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>
    where TMessage : class
{
    /// <summary>Start consuming messages. Blocks until cancellation.</summary>
    Task StartAsync(
        Func<TMessage, CancellationToken, Task> handler,
        CancellationToken ct = default);

    /// <summary>Stop consuming.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Consumer group name.</summary>
    string GroupName { get; }

    /// <summary>Consumer instance name (unique per instance).</summary>
    string ConsumerName { get; }
}

/// <summary>
/// Options for competing consumers.
/// </summary>
public sealed class CompetingConsumerOptions
{
    /// <summary>Consumer group name. All consumers in the same group compete for messages.</summary>
    public string GroupName { get; set; } = "default";

    /// <summary>Unique consumer instance name. Defaults to hostname + random suffix.</summary>
    public string? ConsumerName { get; set; }

    /// <summary>Max concurrent messages per consumer instance. Default: 1 (serial).</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>How long to wait for new messages before polling again. Default: 1s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Max messages to fetch per poll. Default: 10.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Message visibility timeout (for re-delivery on failure). Default: 30s.</summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Max delivery attempts before sending to DLQ. Default: 3.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    internal string ResolvedConsumerName =>
        ConsumerName ?? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
}
