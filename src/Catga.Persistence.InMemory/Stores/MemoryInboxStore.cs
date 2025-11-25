using Catga.Common;
using Catga.Core;
using System.Diagnostics.Metrics;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Inbox;

/// <summary>In-memory inbox store (AOT compatible)</summary>
public class MemoryInboxStore : BaseMemoryStore<InboxMessage>, IInboxStore
{
    private readonly IResiliencePipelineProvider _provider;
    public MemoryInboxStore(IResiliencePipelineProvider? provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }
    public ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            ValidationHelper.ValidateMessageId(messageId);

            if (TryGetMessage(messageId, out var existingMessage) && existingMessage != null)
            {
                if (existingMessage.Status == InboxStatus.Processed) return new ValueTask<bool>(false);
                if (existingMessage.LockExpiresAt.HasValue && existingMessage.LockExpiresAt.Value > DateTime.UtcNow) return new ValueTask<bool>(false);

                existingMessage.Status = InboxStatus.Processing;
                existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);
                CatgaDiagnostics.InboxLocksAcquired.Add(1);
                return new ValueTask<bool>(true);
            }

            var newMessage = new InboxMessage
            {
                MessageId = messageId,
                MessageType = string.Empty,
                Payload = string.Empty,
                Status = InboxStatus.Processing,
                LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
            };

            AddOrUpdateMessage(messageId, newMessage);
            CatgaDiagnostics.InboxLocksAcquired.Add(1);
            return new ValueTask<bool>(true);
        }, cancellationToken);

    public ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            ValidationHelper.ValidateNotNull(message);

            if (TryGetMessage(message.MessageId, out var existing) && existing != null)
            {
                existing.MessageType = message.MessageType;
                existing.Payload = message.Payload;
                existing.ProcessedAt = DateTime.UtcNow;
                existing.ProcessingResult = message.ProcessingResult;
                existing.Status = InboxStatus.Processed;
                existing.LockExpiresAt = null;
                existing.CorrelationId = message.CorrelationId;
                existing.Metadata = message.Metadata;
            }
            else
            {
                message.ProcessedAt = DateTime.UtcNow;
                message.Status = InboxStatus.Processed;
                message.LockExpiresAt = null;
                AddOrUpdateMessage(message.MessageId, message);
            }

            CatgaDiagnostics.InboxProcessed.Add(1);
            return ValueTask.CompletedTask;
        }, cancellationToken);

    public ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct => new ValueTask<bool>(
            TryGetMessage(messageId, out var message) && message != null && message.Status == InboxStatus.Processed), cancellationToken);

    public ValueTask<string?> GetProcessedResultAsync(long messageId, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct => new ValueTask<string?>(
            TryGetMessage(messageId, out var message) && message != null && message.Status == InboxStatus.Processed ? message.ProcessingResult : null), cancellationToken);

    public ValueTask ReleaseLockAsync(long messageId, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct =>
        {
            if (TryGetMessage(messageId, out var message) && message != null)
            {
                message.Status = InboxStatus.Pending;
                message.LockExpiresAt = null;
                CatgaDiagnostics.InboxLocksReleased.Add(1);
            }
            return ValueTask.CompletedTask;
        }, cancellationToken);

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        => _provider.ExecutePersistenceAsync(ct => DeleteExpiredMessagesAsync(
            retentionPeriod,
            m => m.ProcessedAt,
            m => m.Status == InboxStatus.Processed,
            ct), cancellationToken);

    public int GetMessageCountByStatus(InboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

