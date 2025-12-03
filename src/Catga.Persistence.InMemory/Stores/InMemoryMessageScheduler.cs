using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Scheduling;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory message scheduler for development and testing.
/// Thread-safe implementation with automatic delivery.
/// </summary>
public sealed class InMemoryMessageScheduler : IMessageScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, ScheduledEntry> _messages = new();
    private readonly ICatgaMediator _mediator;
    private readonly Timer _timer;

    public InMemoryMessageScheduler(ICatgaMediator mediator)
    {
        _mediator = mediator;
        _timer = new Timer(ProcessDueMessages, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        DateTimeOffset deliverAt,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        var id = Guid.NewGuid().ToString("N");
        var typeName = TypeNameCache<TMessage>.Name;
        _messages[id] = new ScheduledEntry(message, deliverAt, DateTime.UtcNow, typeName, ScheduledMessageStatus.Pending);

        return ValueTask.FromResult(new ScheduledMessageHandle
        {
            ScheduleId = id,
            DeliverAt = deliverAt,
            MessageType = typeName
        });
    }

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        return ScheduleAsync(message, DateTimeOffset.UtcNow + delay, ct);
    }

    public ValueTask<bool> CancelAsync(string scheduleId, CancellationToken ct = default)
    {
        if (_messages.TryGetValue(scheduleId, out var entry) && entry.Status == ScheduledMessageStatus.Pending)
        {
            return ValueTask.FromResult(_messages.TryRemove(scheduleId, out _));
        }
        return ValueTask.FromResult(false);
    }

    public ValueTask<ScheduledMessageInfo?> GetAsync(string scheduleId, CancellationToken ct = default)
    {
        if (_messages.TryGetValue(scheduleId, out var entry))
        {
            return ValueTask.FromResult<ScheduledMessageInfo?>(new ScheduledMessageInfo
            {
                ScheduleId = scheduleId,
                DeliverAt = entry.DeliverAt,
                CreatedAt = entry.CreatedAt,
                MessageType = entry.MessageType,
                Status = entry.Status
            });
        }
        return ValueTask.FromResult<ScheduledMessageInfo?>(null);
    }

    public async IAsyncEnumerable<ScheduledMessageInfo> ListPendingAsync(
        int limit = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var count = 0;
        foreach (var kvp in _messages)
        {
            if (ct.IsCancellationRequested || count >= limit) yield break;
            if (kvp.Value.Status == ScheduledMessageStatus.Pending)
            {
                count++;
                yield return new ScheduledMessageInfo
                {
                    ScheduleId = kvp.Key,
                    DeliverAt = kvp.Value.DeliverAt,
                    CreatedAt = kvp.Value.CreatedAt,
                    MessageType = kvp.Value.MessageType,
                    Status = kvp.Value.Status
                };
            }
        }
        await Task.CompletedTask;
    }

    private void ProcessDueMessages(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _messages)
        {
            if (kvp.Value.DeliverAt <= now && kvp.Value.Status == ScheduledMessageStatus.Pending)
            {
                if (_messages.TryRemove(kvp.Key, out var entry))
                {
                    _ = DeliverAsync(entry);
                }
            }
        }
    }

    private async Task DeliverAsync(ScheduledEntry entry)
    {
        try
        {
            if (entry.Message is IEvent evt)
            {
                await _mediator.PublishAsync(evt);
            }
        }
        catch
        {
            // Log error in production
        }
    }

    public void Dispose() => _timer.Dispose();

    private readonly record struct ScheduledEntry(
        IMessage Message,
        DateTimeOffset DeliverAt,
        DateTimeOffset CreatedAt,
        string MessageType,
        ScheduledMessageStatus Status);

    private static class TypeNameCache<T>
    {
        public static readonly string Name = typeof(T).Name;
    }
}
