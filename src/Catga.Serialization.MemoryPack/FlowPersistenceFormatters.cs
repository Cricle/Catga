using System.Diagnostics.CodeAnalysis;
using Catga.Flow;
using Catga.Flow.Dsl;
using MemoryPack;
using NatsStoredSnapshot = Catga.Persistence.Nats.Stores.StoredSnapshot;

namespace Catga.Serialization.MemoryPack;

public sealed class StoredSnapshotFormatter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState> : MemoryPackFormatter<StoredSnapshot<TState>>
    where TState : class, IFlowState
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref StoredSnapshot<TState>? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(10);
        writer.WriteString(value.FlowId);
        writer.WriteString(value.TypeName);
        writer.WriteArray(MemoryPackSerializer.Serialize(value.State));
        writer.WriteArray(value.PositionPath);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteString(value.Error);
        writer.WriteString(value.WaitConditionId);
        writer.WriteUnmanaged(value.CreatedAt);
        writer.WriteUnmanaged(value.UpdatedAt);
        writer.WriteUnmanaged(value.Version);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref StoredSnapshot<TState>? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        var flowId = reader.ReadString()!;
        var typeName = reader.ReadString()!;
        var stateBytes = reader.ReadArray<byte>()!;
        var positionPath = reader.ReadArray<int>() ?? [];
        reader.ReadUnmanaged(out byte status);
        var error = reader.ReadString();
        var waitConditionId = reader.ReadString();
        reader.ReadUnmanaged(out DateTime createdAt);
        reader.ReadUnmanaged(out DateTime updatedAt);
        reader.ReadUnmanaged(out int version);

        var actualStateType = ResolveStateType(typeName);
        if (!CanAssignToRequestedStateType(actualStateType))
        {
            value = null;
            return;
        }

        var deserializedState = actualStateType == null || actualStateType == typeof(TState)
            ? MemoryPackSerializer.Deserialize<TState>(stateBytes)
            : MemoryPackSerializer.Deserialize(actualStateType, stateBytes) as TState;

        if (deserializedState == null)
            throw new InvalidOperationException($"Failed to deserialize flow state {typeName} as {typeof(TState).FullName}.");

        value = new StoredSnapshot<TState>(
            flowId,
            typeName,
            deserializedState,
            positionPath,
            (DslFlowStatus)status,
            error,
            waitConditionId,
            createdAt,
            updatedAt,
            version);
    }

    private static bool CanAssignToRequestedStateType(Type? actualStateType)
    {
        if (actualStateType == null)
            return typeof(TState).IsInterface || typeof(TState) == typeof(object);

        return typeof(TState).IsAssignableFrom(actualStateType);
    }

    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Flow state types are persisted from known runtime types and resolved conservatively.")]
    [UnconditionalSuppressMessage("AOT", "IL2057", Justification = "Flow state type names come from previously persisted runtime metadata.")]
    private static Type? ResolveStateType(string typeName)
    {
        if (string.Equals(typeName, typeof(TState).FullName, StringComparison.Ordinal))
            return typeof(TState);

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved != null)
            return resolved;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolved = assembly.GetType(typeName, throwOnError: false);
            if (resolved != null)
                return resolved;
        }

        return null;
    }
}

public sealed class StoredSnapshotMetadataFormatter : MemoryPackFormatter<StoredSnapshotMetadata>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref StoredSnapshotMetadata? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(6);
        writer.WriteString(value.FlowId);
        writer.WriteString(value.TypeName);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteUnmanaged(value.CreatedAt);
        writer.WriteUnmanaged(value.UpdatedAt);
        writer.WriteUnmanaged(value.Version);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref StoredSnapshotMetadata? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        var flowId = reader.ReadString()!;
        var typeName = reader.ReadString()!;
        reader.ReadUnmanaged(out byte status);
        reader.ReadUnmanaged(out DateTime createdAt);
        reader.ReadUnmanaged(out DateTime updatedAt);
        reader.ReadUnmanaged(out int version);

        value = new StoredSnapshotMetadata(
            flowId,
            typeName,
            (DslFlowStatus)status,
            createdAt,
            updatedAt,
            version);
    }
}

public sealed class NatsStoredSnapshotFormatter : MemoryPackFormatter<NatsStoredSnapshot>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref NatsStoredSnapshot? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(5);
        writer.WriteString(value.StreamId);
        writer.WriteUnmanaged(value.Version);
        writer.WriteUnmanaged(value.Timestamp);
        writer.WriteString(value.AggregateType);
        writer.WriteArray(value.State);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref NatsStoredSnapshot? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        var streamId = reader.ReadString() ?? string.Empty;
        reader.ReadUnmanaged(out long version);
        reader.ReadUnmanaged(out DateTime timestamp);
        var aggregateType = reader.ReadString() ?? string.Empty;
        var state = reader.ReadArray<byte>();

        value = new NatsStoredSnapshot
        {
            StreamId = streamId,
            Version = version,
            Timestamp = timestamp,
            AggregateType = aggregateType,
            State = state
        };
    }
}

public sealed class ForEachProgressFormatter : MemoryPackFormatter<ForEachProgress>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref ForEachProgress? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(4);
        writer.WriteUnmanaged(value.CurrentIndex);
        writer.WriteUnmanaged(value.TotalCount);
        writer.WriteArray([.. value.CompletedIndices]);
        writer.WriteArray([.. value.FailedIndices]);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref ForEachProgress? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out int currentIndex);
        reader.ReadUnmanaged(out int totalCount);
        var completedIndices = reader.ReadArray<int>() ?? [];
        var failedIndices = reader.ReadArray<int>() ?? [];

        value = new ForEachProgress
        {
            CurrentIndex = currentIndex,
            TotalCount = totalCount,
            CompletedIndices = [.. completedIndices],
            FailedIndices = [.. failedIndices]
        };
    }
}
