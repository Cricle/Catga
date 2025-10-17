using System.Runtime.CompilerServices;

namespace Catga.Messages;

/// <summary>
/// Extension methods for message creation with proper ID generation.
/// </summary>
public static class MessageExtensions
{
    /// <summary>
    /// Generates a new MessageId as a string (for use in message properties).
    /// Uses base32-encoded Guid for shorter strings and better performance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string NewMessageId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Generates a new CorrelationId as a string (for use in message properties).
    /// Uses base32-encoded Guid for shorter strings and better performance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N");
}

