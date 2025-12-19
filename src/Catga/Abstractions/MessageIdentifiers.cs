using System.Runtime.CompilerServices;
using Catga.DistributedId;

namespace Catga.Abstractions;

/// <summary>Message identifier (Snowflake-based)</summary>
public sealed record MessageId(long Value)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    public static MessageId Parse(string value) => new(long.Parse(value));
    public static implicit operator long(MessageId id) => id.Value;
    public static explicit operator MessageId(long value) => new(value);
}

/// <summary>Correlation identifier (Snowflake-based)</summary>
public sealed record CorrelationId(long Value)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId NewId(IDistributedIdGenerator generator) => new(generator.NextId());

    public static CorrelationId Parse(string value) => new(long.Parse(value));
    public override string ToString() => Value.ToString();
    public static implicit operator long(CorrelationId id) => id.Value;
    public static explicit operator CorrelationId(long value) => new(value);
}
