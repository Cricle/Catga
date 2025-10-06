using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace Catga.ObjectPool;

/// <summary>
/// 🔥 对象池 - 复用频繁创建的对象，减少 GC 压力
/// </summary>
public static class CatgaObjectPools
{
    /// <summary>
    /// StringBuilder 对象池
    /// </summary>
    public static readonly ConcurrentBag<StringBuilder> StringBuilderPool = new();

    /// <summary>
    /// 最大池大小
    /// </summary>
    private const int MaxPoolSize = 100;

    /// <summary>
    /// 租用 StringBuilder
    /// </summary>
    public static StringBuilder RentStringBuilder()
    {
        if (StringBuilderPool.TryTake(out var sb))
        {
            sb.Clear();
            return sb;
        }
        return new StringBuilder(256);
    }

    /// <summary>
    /// 归还 StringBuilder
    /// </summary>
    public static void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb.Capacity <= 4096 && StringBuilderPool.Count < MaxPoolSize)
        {
            sb.Clear();
            StringBuilderPool.Add(sb);
        }
    }

    /// <summary>
    /// 租用字节数组
    /// </summary>
    public static byte[] RentBuffer(int minimumLength)
    {
        return ArrayPool<byte>.Shared.Rent(minimumLength);
    }

    /// <summary>
    /// 归还字节数组
    /// </summary>
    public static void ReturnBuffer(byte[] buffer, bool clearArray = false)
    {
        ArrayPool<byte>.Shared.Return(buffer, clearArray);
    }

    /// <summary>
    /// 租用字符数组
    /// </summary>
    public static char[] RentCharBuffer(int minimumLength)
    {
        return ArrayPool<char>.Shared.Rent(minimumLength);
    }

    /// <summary>
    /// 归还字符数组
    /// </summary>
    public static void ReturnCharBuffer(char[] buffer, bool clearArray = false)
    {
        ArrayPool<char>.Shared.Return(buffer, clearArray);
    }
}

/// <summary>
/// 使用 using 语句自动归还的 StringBuilder 包装器
/// </summary>
public ref struct PooledStringBuilder
{
    private StringBuilder _stringBuilder;
    private bool _disposed;

    public PooledStringBuilder()
    {
        _stringBuilder = CatgaObjectPools.RentStringBuilder();
        _disposed = false;
    }

    public StringBuilder StringBuilder => _stringBuilder;

    public void Dispose()
    {
        if (!_disposed)
        {
            CatgaObjectPools.ReturnStringBuilder(_stringBuilder);
            _disposed = true;
        }
    }

    public override string ToString() => _stringBuilder.ToString();
}

/// <summary>
/// 使用 using 语句自动归还的字节数组包装器
/// </summary>
public ref struct PooledBuffer
{
    private byte[] _buffer;
    private bool _disposed;

    public PooledBuffer(int minimumLength)
    {
        _buffer = CatgaObjectPools.RentBuffer(minimumLength);
        _disposed = false;
    }

    public byte[] Buffer => _buffer;
    public Span<byte> Span => _buffer.AsSpan();
    public Memory<byte> Memory => _buffer.AsMemory();

    public void Dispose()
    {
        if (!_disposed)
        {
            CatgaObjectPools.ReturnBuffer(_buffer);
            _disposed = true;
        }
    }
}

