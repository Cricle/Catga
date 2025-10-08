using System.Runtime.CompilerServices;
using Catga.Messages;

namespace Catga.Common;

/// <summary>
/// Common helper methods for message operations
/// Reduces code duplication across behaviors and stores
/// </summary>
public static class MessageHelper
{
    /// <summary>
    /// Generate message ID from request or create new one
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetOrGenerateMessageId<TRequest>(TRequest request) where TRequest : class
    {
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
            return message.MessageId;

        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Get message type name (AOT-friendly)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetMessageType<TRequest>() =>
        typeof(TRequest).AssemblyQualifiedName ??
        typeof(TRequest).FullName ??
        typeof(TRequest).Name;

    /// <summary>
    /// Get correlation ID from request
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? GetCorrelationId<TRequest>(TRequest request) where TRequest : class =>
        request is IMessage message ? message.CorrelationId : null;

    /// <summary>
    /// Validate message ID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessageId(string? messageId, string paramName = "messageId")
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("MessageId is required", paramName);
    }
}
