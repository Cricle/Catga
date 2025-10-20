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
    /// Optimized with Span to reduce allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string NewMessageId()
    {
        Span<char> buffer = stackalloc char[32];  // Guid "N" format = 32 chars
        var guid = Guid.NewGuid();
        guid.TryFormat(buffer, out _, "N");
        return new string(buffer);  // Single allocation
    }

    /// <summary>
    /// Generates a new CorrelationId as a string (for use in message properties).
    /// Uses base32-encoded Guid for shorter strings and better performance.
    /// Optimized with Span to reduce allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string NewCorrelationId()
    {
        Span<char> buffer = stackalloc char[32];  // Guid "N" format = 32 chars
        var guid = Guid.NewGuid();
        guid.TryFormat(buffer, out _, "N");
        return new string(buffer);  // Single allocation
    }
}

