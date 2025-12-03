using Catga.Common;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Outbox;

/// <summary>In-memory outbox store.</summary>
public class MemoryOutboxStore(IResiliencePipelineProvider provider) : BaseMemoryStore<OutboxMessage>, IOutboxStore
{
    private static readonly Comparer<OutboxMessage> ByCreatedAt = Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

    public ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ =>
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.MessageId == 0) throw new ArgumentException("MessageId must be > 0");
            AddOrUpdateMessage(message.MessageId, message);
            CatgaDiagnostics.OutboxAdded.Add(1);
            return ValueTask.CompletedTask;
        }, ct);

    public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => new ValueTask<IReadOnlyList<OutboxMessage>>(
            GetMessagesByPredicate(m => m.Status == OutboxStatus.Pending && m.RetryCount < m.MaxRetries, maxCount, ByCreatedAt)), ct);

    public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => { ExecuteIfExistsAsync(messageId, m => { m.Status = OutboxStatus.Published; m.PublishedAt = DateTime.UtcNow; CatgaDiagnostics.OutboxPublished.Add(1); }); return ValueTask.CompletedTask; }, ct);

    public ValueTask MarkAsFailedAsync(long messageId, string error, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ => { ExecuteIfExistsAsync(messageId, m => { m.RetryCount++; m.LastError = error; m.Status = m.RetryCount >= m.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending; CatgaDiagnostics.OutboxFailed.Add(1); }); return ValueTask.CompletedTask; }, ct);

    public ValueTask DeletePublishedMessagesAsync(TimeSpan retention, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(c => DeleteExpiredMessagesAsync(retention, m => m.PublishedAt, m => m.Status == OutboxStatus.Published, c), ct);

    public int GetMessageCountByStatus(OutboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

