namespace Catga.Persistence.InMemory;

/// <summary>
/// Unified options for all InMemory persistence stores.
/// </summary>
public class InMemoryPersistenceOptions
{
    /// <summary>Retention period for idempotency records. Default: 24 hours.</summary>
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromHours(24);
}
