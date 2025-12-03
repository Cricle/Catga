using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Scheduling;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory message scheduler with automatic delivery.</summary>
public sealed class InMemoryMessageScheduler(ICatgaMediator mediator) : IMessageScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, (IMessage Msg, DateTimeOffset At, DateTimeOffset Created, string Type)> _msgs = new();
    private readonly Timer _timer = new(s => ((InMemoryMessageScheduler)s!).ProcessDue(), null, 1000, 1000);

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, DateTimeOffset deliverAt, CancellationToken ct = default) where TMessage : class, IMessage
    {
        var id = Guid.NewGuid().ToString("N");
        var type = typeof(TMessage).Name;
        _msgs[id] = (message, deliverAt, DateTimeOffset.UtcNow, type);
        return ValueTask.FromResult(new ScheduledMessageHandle { ScheduleId = id, DeliverAt = deliverAt, MessageType = type });
    }

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TimeSpan delay, CancellationToken ct = default) where TMessage : class, IMessage
        => ScheduleAsync(message, DateTimeOffset.UtcNow + delay, ct);

    public ValueTask<bool> CancelAsync(string scheduleId, CancellationToken ct = default)
        => ValueTask.FromResult(_msgs.TryRemove(scheduleId, out _));

    public ValueTask<ScheduledMessageInfo?> GetAsync(string scheduleId, CancellationToken ct = default)
        => ValueTask.FromResult(_msgs.TryGetValue(scheduleId, out var e)
            ? new ScheduledMessageInfo { ScheduleId = scheduleId, DeliverAt = e.At, CreatedAt = e.Created, MessageType = e.Type, Status = ScheduledMessageStatus.Pending }
            : (ScheduledMessageInfo?)null);

    public async IAsyncEnumerable<ScheduledMessageInfo> ListPendingAsync(int limit = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var n = 0;
        foreach (var (k, v) in _msgs)
        {
            if (ct.IsCancellationRequested || n++ >= limit) yield break;
            yield return new() { ScheduleId = k, DeliverAt = v.At, CreatedAt = v.Created, MessageType = v.Type, Status = ScheduledMessageStatus.Pending };
        }
        await Task.CompletedTask;
    }

    private void ProcessDue()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (k, v) in _msgs)
            if (v.At <= now && _msgs.TryRemove(k, out var e) && e.Msg is IEvent evt)
                _ = mediator.PublishAsync(evt);
    }

    public void Dispose() => _timer.Dispose();
}
