using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Catga.Pooling;
using Catga.Serialization;

namespace Catga.Serialization.Json;

/// <summary>
/// JSON serializer with pooling support (System.Text.Json, zero-copy, high-performance)
/// </summary>
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
/// - Inherits from MessageSerializerBase for zero-allocation serialization
/// - Uses MemoryPoolManager for pooled buffer management
/// - Supports Span/Memory for zero-copy deserialization
/// - All pooling logic provided by base class (DRY principle)
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Non-generic methods use reflection and are marked as non-AOT. Generic methods are AOT-safe.")]
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "Non-generic methods use reflection and are marked as non-AOT. Generic methods are AOT-safe.")]
public class JsonMessageSerializer : MessageSerializerBase
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Create JSON serializer with default options (uses reflection, not AOT-compatible)</summary>
    public JsonMessageSerializer()
        : this(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false }) { }

    /// <summary>Create JSON serializer with custom options (for AOT, provide options with JsonSerializerContext)</summary>
    public JsonMessageSerializer(JsonSerializerOptions options)
        : this(options, null) { }

    /// <summary>Create JSON serializer with custom options and pool manager</summary>
    public JsonMessageSerializer(JsonSerializerOptions options, MemoryPoolManager? poolManager)
        : base(poolManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override string Name => "JSON";

    #region Core Methods (Required by Base Class)

    /// <summary>
    /// Core serialization - serialize value to buffer writer
    /// </summary>
    public override void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    /// <summary>
    /// Core deserialization - deserialize value from span
    /// </summary>
    public override T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options)!;
    }

    /// <summary>
    /// Estimate serialized size for buffer allocation
    /// </summary>
    public override int GetSizeEstimate<T>(T value) => 256;

    #endregion

    #region Non-Generic Overloads (System.Text.Json Support)

    /// <summary>
    /// Non-generic serialization (uses reflection, not AOT-compatible)
    /// </summary>
    public override byte[] Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        using var bufferWriter = PoolManager.RentBufferWriter(256);
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
        return bufferWriter.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Non-generic deserialization (uses reflection, not AOT-compatible)
    /// </summary>
    public override object? Deserialize(
        byte[] data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    /// <summary>
    /// Non-generic serialization to buffer (uses reflection, not AOT-compatible)
    /// </summary>
    public override void Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
    }

    /// <summary>
    /// Non-generic deserialization from span (uses reflection, not AOT-compatible)
    /// </summary>
    public override object? Deserialize(
        ReadOnlySpan<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    /// <summary>
    /// Batch serialization - serialize multiple values as JSON array
    /// </summary>
    public override int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> values,
        IBufferWriter<byte> bufferWriter)
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

    #endregion
}
