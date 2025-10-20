using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Messages;

namespace Catga.Core;

/// <summary>
/// Validation helper for argument checking (DRY principle, AOT-safe)
/// All methods are stateless, thread-safe, and marked with AggressiveInlining.
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validate message (null check + MessageId validation for IMessage)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessage<T>(
        [NotNull] T? message,
        [CallerArgumentExpression(nameof(message))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message, paramName);

        if (message is IMessage msg && msg.MessageId == 0)
            throw new ArgumentException("MessageId must be > 0", paramName);
    }

    /// <summary>
    /// Validate MessageId (must be > 0)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessageId(
        long messageId,
        [CallerArgumentExpression(nameof(messageId))] string? paramName = null)
    {
        if (messageId == 0)
            throw new ArgumentException("MessageId must be > 0", paramName);
    }

    /// <summary>
    /// Validate messages collection (null + non-empty check)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessages<T>(
        [NotNull] IEnumerable<T>? messages,
        [CallerArgumentExpression(nameof(messages))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(messages, paramName);

        if (messages is ICollection<T> collection)
        {
            if (collection.Count == 0)
                throw new ArgumentException("Messages collection cannot be empty", paramName);
        }
        else
        {
            // Manual enumeration instead of LINQ Any()
            using var enumerator = messages.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new ArgumentException("Messages collection cannot be empty", paramName);
        }
    }

    /// <summary>
    /// Validate object is not null
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateNotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
    }

    /// <summary>
    /// Validate string is not null or empty
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateNotNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty", paramName);
    }

    /// <summary>
    /// Validate string is not null, empty, or whitespace
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateNotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null, empty, or whitespace", paramName);
    }
}
