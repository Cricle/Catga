using Catga.Common;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Inbox;

/// <summary>In-memory inbox store.</summary>
public class MemoryInboxStore(IResiliencePipelineProvider provider) : BaseMemoryStore<InboxMessage>, IInboxStore
{
    public ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ =>
        {
            if (messageId == 0) throw new ArgumentException("MessageId must be > 0");
            var now = DateTime.UtcNow;
            if (TryGetMessage(messageId, out var m) && m != null)
            {
                if (m.Status == InboxStatus.Processed || (m.LockExpiresAt > now)) return new ValueTask<bool>(false);
                m.Status = InboxStatus.Processing;
                m.LockExpiresAt = now.Add(lockDuration);
            }
            else
            {
                AddOrUpdateMessage(messageId, new() { MessageId = messageId, MessageType = "", Payload = [], Status = InboxStatus.Processing, LockExpiresAt = now.Add(lockDuration) });
            }
            CatgaDiagnostics.InboxLocksAcquired.Add(1);
            return new ValueTask<bool>(true);
        }, ct);

    public ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ =>
        {
            ArgumentNullException.ThrowIfNull(message);
            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;
            AddOrUpdateMessage(message.MessageId, message);
            CatgaDiagnostics.InboxProcessed.Add(1);
            return ValueTask.CompletedTask;
        }, ct);

    public ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => new ValueTask<bool>(TryGetMessage(messageId, out var m) && m?.Status == InboxStatus.Processed), ct);

    public ValueTask<byte[]?> GetProcessedResultAsync(long messageId, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => new ValueTask<byte[]?>(TryGetMessage(messageId, out var m) && m?.Status == InboxStatus.Processed ? m.ProcessingResult : null), ct);

    public ValueTask ReleaseLockAsync(long messageId, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => { ExecuteIfExistsAsync(messageId, m => { m.Status = InboxStatus.Pending; m.LockExpiresAt = null; CatgaDiagnostics.InboxLocksReleased.Add(1); }); return ValueTask.CompletedTask; }, ct);

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retention, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(c => DeleteExpiredMessagesAsync(retention, m => m.ProcessedAt, m => m.Status == InboxStatus.Processed, c), ct);

    public int GetMessageCountByStatus(InboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

