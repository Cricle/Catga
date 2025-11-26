using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using MemoryPack;

namespace Catga.DeadLetter;

/// <summary>
/// Dead letter queue for failed messages (AOT-compatible)
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Send message to dead letter queue
    /// </summary>
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default)
        where TMessage : IMessage;

    /// <summary>
    /// Get failed messages for inspection
    /// </summary>
    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dead letter message envelope (zero-allocation struct)
/// </summary>
[MemoryPackable]
public partial struct DeadLetterMessage
{
    public required long MessageId { get; set; }
    public required string MessageType { get; set; }
    public required string MessageJson { get; set; }
    public required string ExceptionType { get; set; }
    public required string ExceptionMessage { get; set; }
    public required string StackTrace { get; set; }
    public int RetryCount { get; set; }
    public DateTime FailedAt { get; set; }
}
