using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Centralized Metrics for Catga framework
/// Follows OpenTelemetry semantic conventions for messaging systems
/// </summary>
public sealed class CatgaMetrics
{
    private static readonly Meter Meter = new("Catga", "1.0.0");

    // Counters
    private static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>(
        "catga.messages.published",
        unit: "messages",
        description: "Total number of messages published");

    private static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "catga.messages.sent",
        unit: "messages",
        description: "Total number of messages sent");

    private static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>(
        "catga.messages.received",
        unit: "messages",
        description: "Total number of messages received");

    private static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>(
        "catga.messages.processed",
        unit: "messages",
        description: "Total number of messages processed successfully");

    private static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>(
        "catga.messages.failed",
        unit: "messages",
        description: "Total number of messages failed to process");

    private static readonly Counter<long> OutboxMessages = Meter.CreateCounter<long>(
        "catga.outbox.messages",
        unit: "messages",
        description: "Total number of outbox messages");

    private static readonly Counter<long> InboxMessages = Meter.CreateCounter<long>(
        "catga.inbox.messages",
        unit: "messages",
        description: "Total number of inbox messages");

    private static readonly Counter<long> EventsAppended = Meter.CreateCounter<long>(
        "catga.events.appended",
        unit: "events",
        description: "Total number of events appended to event store");

    // Histograms
    private static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
        "catga.message.processing.duration",
        unit: "ms",
        description: "Message processing duration in milliseconds");

    private static readonly Histogram<double> OutboxProcessingDuration = Meter.CreateHistogram<double>(
        "catga.outbox.processing.duration",
        unit: "ms",
        description: "Outbox message processing duration in milliseconds");

    private static readonly Histogram<long> MessageSize = Meter.CreateHistogram<long>(
        "catga.message.size",
        unit: "bytes",
        description: "Message payload size in bytes");

    // Gauges (Observable)
    private static readonly ObservableGauge<int> ActiveHandlers = Meter.CreateObservableGauge<int>(
        "catga.handlers.active",
        () => _activeHandlers,
        unit: "handlers",
        description: "Number of currently active message handlers");

    private static int _activeHandlers;

    /// <summary>
    /// Record a published message
    /// </summary>
    public static void RecordMessagePublished(string messageType, string? system = null)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType }
        };

        if (!string.IsNullOrEmpty(system))
            tags.Add(CatgaActivitySource.Tags.MessagingSystem, system);

        MessagesPublished.Add(1, tags);
    }

    /// <summary>
    /// Record a sent message
    /// </summary>
    public static void RecordMessageSent(string messageType, string? system = null)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType }
        };

        if (!string.IsNullOrEmpty(system))
            tags.Add(CatgaActivitySource.Tags.MessagingSystem, system);

        MessagesSent.Add(1, tags);
    }

    /// <summary>
    /// Record a received message
    /// </summary>
    public static void RecordMessageReceived(string messageType, string? system = null)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType }
        };

        if (!string.IsNullOrEmpty(system))
            tags.Add(CatgaActivitySource.Tags.MessagingSystem, system);

        MessagesReceived.Add(1, tags);
    }

    /// <summary>
    /// Record a successfully processed message
    /// </summary>
    public static void RecordMessageProcessed(string messageType, string handler, double durationMs)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType },
            { CatgaActivitySource.Tags.Handler, handler }
        };

        MessagesProcessed.Add(1, tags);
        MessageProcessingDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record a failed message
    /// </summary>
    public static void RecordMessageFailed(string messageType, string handler, string errorType)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType },
            { CatgaActivitySource.Tags.Handler, handler },
            { CatgaActivitySource.Tags.ErrorType, errorType }
        };

        MessagesFailed.Add(1, tags);
    }

    /// <summary>
    /// Record outbox message operation
    /// </summary>
    public static void RecordOutboxMessage(string operation, double? durationMs = null)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessagingOperation, operation }
        };

        OutboxMessages.Add(1, tags);

        if (durationMs.HasValue)
            OutboxProcessingDuration.Record(durationMs.Value, tags);
    }

    /// <summary>
    /// Record inbox message operation
    /// </summary>
    public static void RecordInboxMessage(string operation)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessagingOperation, operation }
        };

        InboxMessages.Add(1, tags);
    }

    /// <summary>
    /// Record event store append
    /// </summary>
    public static void RecordEventAppended(string streamId, int eventCount)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.StreamId, streamId }
        };

        EventsAppended.Add(eventCount, tags);
    }

    /// <summary>
    /// Record message size
    /// </summary>
    public static void RecordMessageSize(string messageType, long sizeBytes)
    {
        var tags = new TagList
        {
            { CatgaActivitySource.Tags.MessageType, messageType }
        };

        MessageSize.Record(sizeBytes, tags);
    }

    /// <summary>
    /// Increment active handlers counter
    /// </summary>
    public static void IncrementActiveHandlers() => Interlocked.Increment(ref _activeHandlers);

    /// <summary>
    /// Decrement active handlers counter
    /// </summary>
    public static void DecrementActiveHandlers() => Interlocked.Decrement(ref _activeHandlers);
}

