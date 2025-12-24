using Catga.Common;
using Catga.Core;
using Catga.Hosting;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Outbox;

/// <summary>In-memory outbox store.</summary>
public class MemoryOutboxStore(IResiliencePipelineProvider provider) : BaseMemoryStore<OutboxMessage>, IOutboxStore, IHealthCheckable, Hosting.IRecoverableComponent
{
    private static readonly Comparer<OutboxMessage> ByCreatedAt = Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
    private bool _isHealthy = true;
    private string? _healthStatus = "Initialized";
    private DateTimeOffset? _lastHealthCheck;
    
    /// <inheritdoc/>
    public bool IsHealthy => _isHealthy;
    
    /// <inheritdoc/>
    public string? HealthStatus => _healthStatus;
    
    /// <inheritdoc/>
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;
    
    /// <inheritdoc/>
    public string ComponentName => "MemoryOutboxStore";

    public ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
        => provider.ExecutePersistenceAsync(_ =>
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.MessageId == 0) throw new ArgumentException("MessageId must be > 0");
            try
            {
                AddOrUpdateMessage(message.MessageId, message);
                CatgaDiagnostics.OutboxAdded.Add(1);
                UpdateHealthStatus(true);
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, ex.Message);
                throw;
            }
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
    
    /// <inheritdoc/>
    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // In-memory store doesn't need recovery, just verify it's operational
            _isHealthy = true;
            _healthStatus = "Healthy";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _healthStatus = $"Recovery failed: {ex.Message}";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }
    
    /// <summary>
    /// Updates health status based on operation results
    /// </summary>
    private void UpdateHealthStatus(bool success, string? errorMessage = null)
    {
        _isHealthy = success;
        _healthStatus = success ? "Healthy" : $"Unhealthy: {errorMessage}";
        _lastHealthCheck = DateTimeOffset.UtcNow;
    }
}

