using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Catga.DistributedId;

namespace Catga.Messages;

/// <summary>
/// High-performance message identifier based on Snowflake distributed ID
/// Replaces Guid-based MessageId with distributed ID for better performance and sortability
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record MessageId : IEquatable<MessageId>
{
    private readonly long _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MessageId(long value) => _value = value;

    /// <summary>
    /// Generate new message ID using distributed ID generator
    /// Requires IDistributedIdGenerator to be registered in DI
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId Parse(string value) => new(long.Parse(value));

    /// <summary>
    /// Get the underlying distributed ID value
    /// </summary>
    public long Value => _value;
    public static implicit operator string(MessageId id) => id.ToString();
    public static implicit operator long(MessageId id) => id._value;
    public static explicit operator MessageId(long value) => new(value);
}

/// <summary>
/// High-performance correlation identifier based on Snowflake distributed ID
/// Used to correlate related messages across distributed systems
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record CorrelationId : IEquatable<CorrelationId>
{
    private readonly long _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CorrelationId(long value) => _value = value;

    /// <summary>
    /// Generate new correlation ID using distributed ID generator
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId Parse(string value) => new(long.Parse(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => _value.ToString();

    /// <summary>
    /// Get the underlying distributed ID value
    /// </summary>
    public long Value => _value;

    public static implicit operator string(CorrelationId id) => id.ToString();
    public static implicit operator long(CorrelationId id) => id._value;
    public static explicit operator CorrelationId(long value) => new(value);
}
