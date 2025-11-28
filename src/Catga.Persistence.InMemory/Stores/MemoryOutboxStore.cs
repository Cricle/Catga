using Catga.Common;
using Catga.Core;
using System.Diagnostics.Metrics;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Outbox;

/// <summary>In-memory outbox store (AOT compatible)</summary>
public class MemoryOutboxStore : BaseMemoryStore<OutboxMessage>, IOutboxStore
{
    private readonly IResiliencePipelineProvider _provider;
    public MemoryOutboxStore(IResiliencePipelineProvider? provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }
    public ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.MessageId == 0)
                throw new ArgumentException("MessageId must be > 0", nameof(message.MessageId));
            AddOrUpdateMessage(message.MessageId, message);
            CatgaDiagnostics.OutboxAdded.Add(1);
            return ValueTask.CompletedTask;
        }, cancellationToken);

    public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct => new ValueTask<IReadOnlyList<OutboxMessage>>(
            GetMessagesByPredicate(
                message => message.Status == OutboxStatus.Pending && message.RetryCount < message.MaxRetries,
                maxCount,
                Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt)))), cancellationToken);

    public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            if (TryGetMessage(messageId, out var message) && message != null)
            {
                message.Status = OutboxStatus.Published;
                message.PublishedAt = DateTime.UtcNow;
                CatgaDiagnostics.OutboxPublished.Add(1);
            }
            return ValueTask.CompletedTask;
        }, cancellationToken);

    public ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            if (TryGetMessage(messageId, out var message) && message != null)
            {
                message.RetryCount++;
                message.LastError = errorMessage;
                message.Status = message.RetryCount >= message.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
                CatgaDiagnostics.OutboxFailed.Add(1);
            }
            return ValueTask.CompletedTask;
        }, cancellationToken);

    public ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct => DeleteExpiredMessagesAsync(
            retentionPeriod,
            m => m.PublishedAt,
            m => m.Status == OutboxStatus.Published,
            ct), cancellationToken);

    public int GetMessageCountByStatus(OutboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

