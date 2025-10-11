namespace Catga;

/// <summary>
/// Message delivery mode - controls at-least-once delivery behavior
/// </summary>
public enum DeliveryMode
{
    /// <summary>
    /// Wait for Result
    /// - Synchronous wait for message processing
    /// - Wait for ACK confirmation
    /// - Blocks until success or failure
    /// - Use cases: operations requiring immediate feedback (payment, order confirmation)
    /// </summary>
    WaitForResult = 0,

    /// <summary>
    /// Async Retry
    /// - Returns immediately without waiting
    /// - Background retry ensures at-least-once delivery
    /// - Uses persistent queue (Outbox, JetStream, Redis Streams)
    /// - Use cases: high throughput, no immediate feedback needed (notification, log, sync)
    /// </summary>
    AsyncRetry = 1
}

