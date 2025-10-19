using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Catga.Pooling;

namespace Catga.Serialization.Json;

/// <summary>JSON serializer with pooling support (System.Text.Json, zero-copy, high-performance)</summary>
/// <remarks>
/// <para>For Native AOT compatibility, provide JsonSerializerOptions with a JsonSerializerContext:</para>
/// <code>
/// [JsonSerializable(typeof(MyMessage))]
/// public partial class MyJsonContext : JsonSerializerContext { }
///
/// var options = new JsonSerializerOptions { TypeInfoResolver = MyJsonContext.Default };
/// services.AddCatga().UseJsonSerializer(new JsonMessageSerializer(options));
/// </code>
/// <para>📖 See docs/aot/serialization-aot-guide.md for complete AOT setup guide.</para>
/// <para>
/// Memory Optimization:
/// - Uses MemoryPoolManager for zero-allocation serialization
/// - Supports Span/Memory for zero-copy deserialization
/// - Pooled buffers with automatic disposal
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Non-generic methods use reflection and are marked as non-AOT. Generic methods are AOT-safe.")]
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "Non-generic methods use reflection and are marked as non-AOT. Generic methods are AOT-safe.")]
public class JsonMessageSerializer : IPooledMessageSerializer
{
    private readonly JsonSerializerOptions _options;
    private readonly MemoryPoolManager _poolManager;

    /// <summary>Create JSON serializer with default options (uses reflection, not AOT-compatible)</summary>
    public JsonMessageSerializer() : this(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false }) { }

    /// <summary>Create JSON serializer with custom options (for AOT, provide options with JsonSerializerContext)</summary>
    public JsonMessageSerializer(JsonSerializerOptions options) : this(options, MemoryPoolManager.Shared) { }

    /// <summary>Create JSON serializer with custom options and pool manager</summary>
    public JsonMessageSerializer(JsonSerializerOptions options, MemoryPoolManager poolManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _poolManager = poolManager ?? throw new ArgumentNullException(nameof(poolManager));
    }

    public string Name => "JSON";

    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        Serialize(value, bufferWriter);
        return bufferWriter.WrittenSpan.ToArray();
    }

    public byte[] Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
        return bufferWriter.WrittenSpan.ToArray();
    }

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    public object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }

    public int GetSizeEstimate<T>(T value) => 256;

    // === IMessageSerializer 新方法 (AOT-safe) ===

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<byte> SerializeToMemory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = _poolManager.RentBufferWriter();
        Serialize(value, writer);
        
        var owner = _poolManager.RentMemory(writer.WrittenCount);
        writer.WrittenSpan.CopyTo(owner.Memory.Span);
        return owner;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlyMemory<byte> data)
        => Deserialize<T>(data.Span);

    /// <inheritdoc />
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return Deserialize<T>(data.FirstSpan);

        // Multi-segment: rent buffer and copy
        using var owner = _poolManager.RentMemory((int)data.Length);
        data.CopyTo(owner.Memory.Span);
        return Deserialize<T>(owner.Memory.Span);
    }

    // === IBufferedMessageSerializer 新方法 ===

    /// <inheritdoc />
    public void Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    /// <inheritdoc />
    public bool TrySerialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            // Use ArrayBufferWriter for small allocations
            using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
            Serialize(value, pooledWriter);
            
            if (pooledWriter.WrittenCount > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            
            pooledWriter.WrittenSpan.CopyTo(destination);
            bytesWritten = pooledWriter.WrittenCount;
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    /// <inheritdoc />
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, Memory<byte> destination, out int bytesWritten)
    {
        using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
        Serialize(value, pooledWriter);
        
        if (pooledWriter.WrittenCount > destination.Length)
            throw new InvalidOperationException($"Destination buffer too small. Required: {pooledWriter.WrittenCount}, Available: {destination.Length}");
        
        pooledWriter.WrittenSpan.CopyTo(destination.Span);
        bytesWritten = pooledWriter.WrittenCount;
    }

    /// <inheritdoc />
    public int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(IEnumerable<T> values, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        
        writer.WriteStartArray();
        foreach (var value in values)
        {
            JsonSerializer.Serialize(writer, value, _options);
        }
        writer.WriteEndArray();
        writer.Flush();
        
        return (int)writer.BytesCommitted;
    }

    // === IPooledMessageSerializer 方法 (AOT-safe) ===

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledBuffer SerializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var owner = SerializeToMemory(value);
        return new PooledBuffer(owner, (int)owner.Memory.Length);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? DeserializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
        => Deserialize<T>(data);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IPooledBufferWriter<byte> GetPooledWriter(int initialCapacity = 256)
        => _poolManager.RentBufferWriter(initialCapacity);
}

