using Catga.Flow;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// Custom MemoryPack formatter for FlowState.
/// Registers automatically via module initializer.
/// </summary>
public sealed class FlowStateFormatter : MemoryPackFormatter<FlowState>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref FlowState? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(9);
        writer.WriteString(value.Id);
        writer.WriteString(value.Type);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteUnmanaged(value.Step);
        writer.WriteUnmanaged(value.Version);
        writer.WriteString(value.Owner);
        writer.WriteUnmanaged(value.HeartbeatAt);
        writer.WriteArray(value.Data);
        writer.WriteString(value.Error);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref FlowState? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        var id = reader.ReadString()!;
        var type = reader.ReadString()!;
        reader.ReadUnmanaged(out byte status);
        reader.ReadUnmanaged(out int step);
        reader.ReadUnmanaged(out long version);
        var owner = reader.ReadString();
        reader.ReadUnmanaged(out long heartbeatAt);
        var data = reader.ReadArray<byte>();
        var error = reader.ReadString();

        value = new FlowState
        {
            Id = id,
            Type = type,
            Status = (FlowStatus)status,
            Step = step,
            Version = version,
            Owner = owner,
            HeartbeatAt = heartbeatAt,
            Data = data is { Length: > 0 } ? data : null,
            Error = error
        };
    }
}

/// <summary>
/// Module initializer to register FlowState formatter.
/// </summary>
file static class FlowStateFormatterInitializer
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Initialize()
    {
        MemoryPackFormatterProvider.Register(new FlowStateFormatter());
    }
}
