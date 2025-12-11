using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;

namespace Catga.Tests.Flow;

// Backward-compatibility helpers for old WaitConditionType-based APIs in tests
public enum WaitConditionType
{
    WhenAll,
    WhenAny
}

public static class WaitConditionTestExtensions
{
    private static WaitType ToWaitType(this WaitConditionType type) =>
        type == WaitConditionType.WhenAll ? WaitType.All : WaitType.Any;

    /// <summary>
    /// Test-only compatibility shim for old IDslFlowStore.SetWaitConditionAsync signature
    /// that used WaitConditionType and a collection of IDs.
    /// </summary>
    public static Task SetWaitConditionAsync(
        this IDslFlowStore store,
        string correlationId,
        WaitConditionType type,
        IReadOnlyCollection<string> childFlowIds,
        DateTime timeoutAt,
        string flowId = "test-flow",
        string flowType = "TestFlow",
        int step = 0,
        bool cancelOthers = false,
        CancellationToken ct = default)
    {
        var condition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = type.ToWaitType(),
            ExpectedCount = childFlowIds.Count,
            CompletedCount = 0,
            Timeout = timeoutAt - DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            FlowId = flowId,
            FlowType = flowType,
            Step = step,
            CancelOthers = cancelOthers,
            ChildFlowIds = childFlowIds.ToList(),
            Results = new List<FlowCompletedEventData>()
        };

        return store.SetWaitConditionAsync(correlationId, condition, ct);
    }

    public static async Task<WaitCondition?> UpdateWaitConditionAsync(
        this IDslFlowStore store,
        string correlationId,
        string childFlowId,
        object? result,
        CancellationToken ct = default)
    {
        var condition = await store.GetWaitConditionAsync(correlationId, ct);
        if (condition == null)
            return null;

        if (condition.Results.Any(r => r.FlowId == childFlowId))
            return condition;

        condition.CompletedCount++;
        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = childFlowId,
            ParentCorrelationId = correlationId,
            Success = true,
            Result = result
        });

        await store.UpdateWaitConditionAsync(correlationId, condition, ct);
        return condition;
    }
}
