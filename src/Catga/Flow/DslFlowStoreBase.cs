using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Flow.Dsl;

namespace Catga.Flow;

/// <summary>
/// Base class for IDslFlowStore implementations.
/// Provides common helper methods for serialization/deserialization across different storage backends.
/// Implementations should inherit from this class and implement IDslFlowStore interface directly.
/// </summary>
public class DslFlowStoreBase
{
    protected readonly IMessageSerializer Serializer;

    protected DslFlowStoreBase(IMessageSerializer serializer)
    {
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Helper method to serialize snapshot.
    /// </summary>
    protected byte[] SerializeSnapshot<TState>(FlowSnapshot<TState> snapshot) where TState : class, IFlowState
    {
        return Serializer.Serialize(snapshot);
    }

    /// <summary>
    /// Helper method to deserialize snapshot.
    /// </summary>
    protected FlowSnapshot<TState>? DeserializeSnapshot<TState>(byte[] data) where TState : class, IFlowState
    {
        return Serializer.Deserialize<FlowSnapshot<TState>>(data);
    }

    /// <summary>
    /// Helper method to serialize wait condition.
    /// </summary>
    protected byte[] SerializeWaitCondition(WaitCondition condition)
    {
        return Serializer.Serialize(condition);
    }

    /// <summary>
    /// Helper method to deserialize wait condition.
    /// </summary>
    protected WaitCondition? DeserializeWaitCondition(byte[] data)
    {
        return Serializer.Deserialize<WaitCondition>(data);
    }

    /// <summary>
    /// Helper method to serialize ForEach progress.
    /// </summary>
    protected byte[] SerializeForEachProgress(ForEachProgress progress)
    {
        return Serializer.Serialize(progress);
    }

    /// <summary>
    /// Helper method to deserialize ForEach progress.
    /// </summary>
    protected ForEachProgress? DeserializeForEachProgress(byte[] data)
    {
        return Serializer.Deserialize<ForEachProgress>(data);
    }

    /// <summary>
    /// Helper method to serialize loop progress.
    /// </summary>
    protected byte[] SerializeLoopProgress(LoopProgress progress)
    {
        return Serializer.Serialize(progress);
    }

    /// <summary>
    /// Helper method to deserialize loop progress.
    /// </summary>
    protected LoopProgress? DeserializeLoopProgress(byte[] data)
    {
        return Serializer.Deserialize<LoopProgress>(data);
    }
}
