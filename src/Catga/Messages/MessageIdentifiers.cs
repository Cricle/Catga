using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Catga.Messages;

/// <summary>
/// 高性能消息标识符（值类型，零分配）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MessageId : IEquatable<MessageId>
{
    private readonly Guid _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MessageId(Guid value) => _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId NewId() => new(Guid.NewGuid());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => _value.ToString("N"); // 无连字符，更快

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(MessageId other) => _value.Equals(other._value);

    public override bool Equals(object? obj) => obj is MessageId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _value.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MessageId left, MessageId right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MessageId left, MessageId right) => !left.Equals(right);

    public static implicit operator string(MessageId id) => id.ToString();
}

/// <summary>
/// 高性能关联标识符（值类型，零分配）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct CorrelationId : IEquatable<CorrelationId>
{
    private readonly Guid _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CorrelationId(Guid value) => _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId NewId() => new(Guid.NewGuid());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CorrelationId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => _value.ToString("N");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(CorrelationId other) => _value.Equals(other._value);

    public override bool Equals(object? obj) => obj is CorrelationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _value.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(CorrelationId left, CorrelationId right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(CorrelationId left, CorrelationId right) => !left.Equals(right);

    public static implicit operator string(CorrelationId id) => id.ToString();
}

