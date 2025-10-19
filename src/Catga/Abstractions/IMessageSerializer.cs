using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

/// <summary>
/// Message serializer interface with modern memory support
/// </summary>
/// <remarks>
/// For AOT compatibility:
/// - Use MemoryPack serializer (built-in AOT support)
/// - Or provide JsonSerializerContext for System.Text.Json
/// - Generic methods are AOT-friendly
/// - Non-generic methods use reflection (not AOT compatible)
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>
    /// Serialize object to byte array (allocates new array)
    /// </summary>
    /// <remarks>AOT-friendly when T is known at compile time</remarks>
    byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value);

    /// <summary>
    /// Serialize object to byte array (non-generic, uses reflection)
    /// </summary>
    /// <remarks>Not AOT-compatible due to reflection. Use generic version for AOT.</remarks>
    byte[] Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    /// <remarks>AOT-friendly when T is known at compile time</remarks>
    T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data);

    /// <summary>
    /// Deserialize object from byte array (non-generic, uses reflection)
    /// </summary>
    /// <remarks>Not AOT-compatible due to reflection. Use generic version for AOT.</remarks>
    object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Serialize to IMemoryOwner (caller must dispose, pooled memory)
    /// </summary>
    /// <remarks>
    /// Zero-allocation when using MemoryPool. Caller is responsible for disposing.
    /// AOT-friendly when T is known at compile time.
    /// </remarks>
    IMemoryOwner<byte> SerializeToMemory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value);

    /// <summary>
    /// Deserialize from ReadOnlyMemory (async-friendly, no copy)
    /// </summary>
    /// <remarks>AOT-friendly when T is known at compile time</remarks>
    T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Deserialize from ReadOnlySequence (pipeline scenarios, multi-segment)
    /// </summary>
    /// <remarks>
    /// Efficient for pipeline scenarios where data may span multiple buffers.
    /// AOT-friendly when T is known at compile time.
    /// </remarks>
    T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data);

    /// <summary>
    /// Serializer name
    /// </summary>
    string Name { get; }
}

