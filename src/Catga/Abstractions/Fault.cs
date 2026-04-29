using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

/// <summary>
/// Fault message published automatically when a handler fails.
/// Subscribe to Fault&lt;TMessage&gt; to handle errors for a specific message type.
/// Equivalent to MassTransit's Fault&lt;T&gt;.
/// </summary>
public sealed class Fault<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>
    : IEvent
    where TMessage : IMessage
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }

    /// <summary>The original message that caused the fault.</summary>
    public TMessage Message { get; init; }

    /// <summary>The exception that caused the fault (if any).</summary>
    public Exception? Exception { get; init; }

    /// <summary>Error code from the failure result.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When the fault occurred.</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Host/node where the fault occurred.</summary>
    public string Host { get; init; } = Environment.MachineName;

    public Fault(TMessage message, Exception? exception = null, string? errorCode = null, string? errorMessage = null)
    {
        Message = message;
        Exception = exception;
        ErrorCode = errorCode ?? exception?.GetType().Name;
        ErrorMessage = errorMessage ?? exception?.Message;
        CorrelationId = message.CorrelationId;
    }
}
