using Catga.Abstractions;
using Catga.Transport;

namespace Catga.Messaging;

/// <summary>
/// Routes messages to different destinations based on message headers/metadata.
/// </summary>
public interface IMessageRouter
{
    /// <summary>Resolve destination for the given transport context.</summary>
    string? Resolve(TransportContext context);

    /// <summary>Add a header-based routing rule.</summary>
    IMessageRouter AddRoute(string headerKey, string headerValue, string destination);
}

/// <summary>
/// Header-based message router. Routes messages to different queues/subjects
/// based on metadata headers in TransportContext.
/// </summary>
public sealed class MessageRouter : IMessageRouter
{
    private readonly List<(string Key, string Value, string Destination)> _routes = [];
    private readonly string? _defaultDestination;

    public MessageRouter(string? defaultDestination = null)
        => _defaultDestination = defaultDestination;

    public IMessageRouter AddRoute(string headerKey, string headerValue, string destination)
    {
        _routes.Add((headerKey, headerValue, destination));
        return this;
    }

    public string? Resolve(TransportContext context)
    {
        if (context.Metadata == null) return _defaultDestination;

        foreach (var (key, value, dest) in _routes)
        {
            if (context.Metadata.TryGetValue(key, out var actual) && actual == value)
                return dest;
        }

        return _defaultDestination;
    }
}

/// <summary>
/// Wraps a TransportContext with priority metadata.
/// Priority is encoded as "x-priority" header (0=Low, 1=Normal, 2=High, 3=Critical).
/// </summary>
public sealed class PriorityTransportContext
{
    public MessagePriority Priority { get; }
    public TransportContext Context { get; }

    public PriorityTransportContext(MessagePriority priority)
    {
        Priority = priority;
        Context = new TransportContext
        {
            Metadata = new Dictionary<string, string>
            {
                ["x-priority"] = ((int)priority).ToString()
            }
        };
    }

    /// <summary>Create a high-priority context.</summary>
    public static PriorityTransportContext High => new(MessagePriority.High);

    /// <summary>Create a critical-priority context.</summary>
    public static PriorityTransportContext Critical => new(MessagePriority.Critical);

    /// <summary>Create a normal-priority context.</summary>
    public static PriorityTransportContext Normal => new(MessagePriority.Normal);
}
