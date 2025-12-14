using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow state persistence interface for DSL flows.
/// </summary>
public interface IDslFlowStore
{
    /// <summary>Create a new flow snapshot.</summary>
    Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Get flow snapshot by ID.</summary>
    Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(string flowId, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Update flow snapshot. Only updates if state.HasChanges.</summary>
    Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Delete flow snapshot.</summary>
    Task<bool> DeleteAsync(string flowId, CancellationToken ct = default);

    /// <summary>Set wait condition for WhenAll/WhenAny.</summary>
    Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default);

    /// <summary>Get wait condition.</summary>
    Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default);

    /// <summary>Update wait condition.</summary>
    Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default);

    /// <summary>Clear wait condition.</summary>
    Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default);

    /// <summary>Get timed out wait conditions.</summary>
    Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default);

    /// <summary>Save ForEach progress for recovery.</summary>
    Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default);

    /// <summary>Get ForEach progress for recovery.</summary>
    Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default);

    /// <summary>Clear ForEach progress.</summary>
    Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default);
}
