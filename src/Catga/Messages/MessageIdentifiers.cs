using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Catga.DistributedId;

namespace Catga.Messages;

/// <summary>Message identifier (Snowflake-based)</summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record MessageId : IEquatable<MessageId>
{
    private readonly long _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MessageId(long value) => _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId Parse(string value) => new(long.Parse(value));

    public long Value => _value;
    public static implicit operator string(MessageId id) => id.ToString();
    public static implicit operator long(MessageId id) => id._value;
    public static explicit operator MessageId(long value) => new(value);
}

/// <summary>Correlation identifier (Snowflake-based)</summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record CorrelationId : IEquatable<CorrelationId>
{
    private readonly long _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CorrelationId(long value) => _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId Parse(string value) => new(long.Parse(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => _value.ToString();

    public long Value => _value;
    public static implicit operator string(CorrelationId id) => id.ToString();
    public static implicit operator long(CorrelationId id) => id._value;
    public static explicit operator CorrelationId(long value) => new(value);
}
