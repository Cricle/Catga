using System.Collections.Concurrent;

namespace Catga.Testing;

/// <summary>
/// Thread-safe capture of published and consumed messages for test assertions.
/// </summary>
public sealed class MessageCapture
{
    private readonly ConcurrentBag<object> _published = new();
    private readonly ConcurrentBag<object> _consumed = new();

    public IReadOnlyList<object> Published => _published.ToList();
    public IReadOnlyList<object> Consumed => _consumed.ToList();

    public void RecordPublished(object message) => _published.Add(message);
    public void RecordConsumed(object message) => _consumed.Add(message);

    public void Clear()
    {
        _published.Clear();
        _consumed.Clear();
    }
}
