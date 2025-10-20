using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Messages;

namespace Catga.Core;

/// <summary>
/// Validation helper for argument checking (DRY principle, AOT-safe)
/// </summary>
/// <remarks>
/// <para>
/// Provides consistent validation across all Catga components, eliminating
/// duplicate ArgumentNullException.ThrowIfNull calls (~46 occurrences).
/// </para>
/// <para>
/// AOT Compatibility: Fully compatible with Native AOT. No reflection.
/// Uses CallerArgumentExpressionAttribute for better error messages.
/// </para>
/// <para>
/// Thread Safety: All methods are stateless and thread-safe.
/// </para>
/// <para>
/// Performance: All methods are marked with AggressiveInlining for zero overhead.
/// </para>
/// </remarks>
public static class ValidationHelper
{
    /// <summary>
    /// Validate message object (null check + MessageId validation for IMessage)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentNullException">Thrown if message is null</exception>
    /// <exception cref="ArgumentException">Thrown if MessageId is invalid for IMessage</exception>
    /// <remarks>
    /// AOT-safe. Uses generic constraint instead of reflection.
    /// Validates both null and MessageId in a single call.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessage<T>(
        [NotNull] T? message,
        [CallerArgumentExpression(nameof(message))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message, paramName);

        // For IMessage, also validate MessageId
        if (message is IMessage msg && string.IsNullOrEmpty(msg.MessageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", paramName);
        }
    }

    /// <summary>
    /// Validate MessageId string (null/empty check)
    /// </summary>
    /// <param name="messageId">MessageId to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentException">Thrown if messageId is null or empty</exception>
    /// <remarks>
    /// AOT-safe. Simple string validation with clear error message.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessageId(
        [NotNull] string? messageId,
        [CallerArgumentExpression(nameof(messageId))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", paramName);
        }
    }

    /// <summary>
    /// Validate messages collection (null + non-empty check)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="messages">Messages collection to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentNullException">Thrown if messages is null</exception>
    /// <exception cref="ArgumentException">Thrown if messages is empty</exception>
    /// <remarks>
    /// <para>
    /// AOT-safe. Avoids multiple enumeration by checking Count first if available.
    /// </para>
    /// <para>
    /// Performance: Uses ICollection&lt;T&gt; Count for O(1) check when possible,
    /// falls back to Any() for IEnumerable&lt;T&gt;.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessages<T>(
        [NotNull] IEnumerable<T>? messages,
        [CallerArgumentExpression(nameof(messages))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(messages, paramName);

        // Optimize for ICollection<T> (O(1) count check)
        if (messages is ICollection<T> collection)
        {
            if (collection.Count == 0)
            {
                throw new ArgumentException("Messages collection cannot be empty", paramName);
            }
        }
        else
        {
            // Fallback to Any() for IEnumerable<T>
            if (!messages.Any())
            {
                throw new ArgumentException("Messages collection cannot be empty", paramName);
            }
        }
    }

    /// <summary>
    /// Validate object with custom error message
    /// </summary>
    /// <typeparam name="T">Object type</typeparam>
    /// <param name="value">Value to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentNullException">Thrown if value is null</exception>
    /// <remarks>
    /// AOT-safe. Generic null check for any reference type.
    /// </remarks>
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
    /// <param name="value">String to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentException">Thrown if value is null or empty</exception>
    /// <remarks>
    /// AOT-safe. String validation with clear error message.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateNotNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty", paramName);
        }
    }

    /// <summary>
    /// Validate string is not null, empty, or whitespace
    /// </summary>
    /// <param name="value">String to validate</param>
    /// <param name="paramName">Parameter name (auto-captured)</param>
    /// <exception cref="ArgumentException">Thrown if value is null, empty, or whitespace</exception>
    /// <remarks>
    /// AOT-safe. String validation with whitespace check.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateNotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace", paramName);
        }
    }
}

