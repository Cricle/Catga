using Catga.Flow.Dsl;
using FsCheck;
using MemoryPack;

namespace Catga.Tests.PropertyTests.Generators;

/// <summary>
/// FsCheck 生成器 - Flow 状态相关类型
/// 用于属性测试的 Flow 数据生成
/// </summary>
public static class FlowStateGenerators
{
    /// <summary>
    /// 生成有效的 Flow ID
    /// </summary>
    public static Arbitrary<string> FlowIdArbitrary()
    {
        return Gen.OneOf(
            // GUID 格式
            Gen.Fresh(() => $"flow-{Guid.NewGuid():N}"),
            // 带类型前缀的格式
            Gen.Elements("order-flow-", "payment-flow-", "shipping-flow-", "approval-flow-")
                .SelectMany(prefix => Gen.Choose(1, 100000).Select(id => $"{prefix}{id}")),
            // 简单格式
            Gen.Choose(1, 100000).Select(id => $"flow-{id}")
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成 Flow 类型名称
    /// </summary>
    public static Arbitrary<string> FlowTypeArbitrary()
    {
        return Gen.Elements(
            "OrderProcessingFlow",
            "PaymentFlow",
            "ShippingFlow",
            "ApprovalFlow",
            "NotificationFlow",
            "RefundFlow",
            "InventoryFlow"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成 DslFlowStatus
    /// </summary>
    public static Arbitrary<DslFlowStatus> FlowStatusArbitrary()
    {
        return Gen.Elements(
            DslFlowStatus.Pending,
            DslFlowStatus.Running,
            DslFlowStatus.Suspended,
            DslFlowStatus.Compensating,
            DslFlowStatus.Completed,
            DslFlowStatus.Failed,
            DslFlowStatus.Cancelled
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成 FlowPosition
    /// </summary>
    public static Arbitrary<FlowPosition> FlowPositionArbitrary()
    {
        return Gen.OneOf(
            // 初始位置
            Gen.Constant(FlowPosition.Initial),
            // 简单位置
            Gen.Choose(0, 20).Select(step => new FlowPosition([step])),
            // 嵌套位置（分支）
            Gen.Choose(0, 10).SelectMany(step1 =>
                Gen.Choose(0, 5).Select(step2 => new FlowPosition([step1, step2])))
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成测试 Flow 状态
    /// </summary>
    public static Arbitrary<TestPropertyFlowState> TestFlowStateArbitrary()
    {
        return Arb.Default.NonEmptyString().Generator
            .SelectMany(orderId =>
                Gen.Choose(0, 100000).SelectMany(amount =>
                    Gen.Elements("Pending", "Processing", "Completed", "Failed").SelectMany(status =>
                        Gen.Choose(1, 5).SelectMany(itemCount =>
                            Gen.ListOf(itemCount, Arb.Default.NonEmptyString().Generator)
                                .Select(items => new TestPropertyFlowState
                                {
                                    OrderId = orderId.Get,
                                    Amount = amount,
                                    Status = status,
                                    Items = items.Select(i => i.Get).ToList()
                                })))))
            .ToArbitrary();
    }


    /// <summary>
    /// 生成 FlowSnapshot TestPropertyFlowState
    /// </summary>
    public static Arbitrary<FlowSnapshot<TestPropertyFlowState>> FlowSnapshotArbitrary()
    {
        return FlowIdArbitrary().Generator
            .SelectMany(flowId =>
                TestFlowStateArbitrary().Generator.SelectMany(state =>
                    FlowPositionArbitrary().Generator.SelectMany(position =>
                        FlowStatusArbitrary().Generator.Select(status =>
                            new FlowSnapshot<TestPropertyFlowState>
                            {
                                FlowId = flowId,
                                State = state,
                                Position = position,
                                Status = status,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            }))))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成 WaitCondition
    /// </summary>
    public static Arbitrary<WaitCondition> WaitConditionArbitrary()
    {
        return Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            .SelectMany(correlationId =>
                FlowIdArbitrary().Generator.SelectMany(flowId =>
                    Gen.Elements(WaitType.All, WaitType.Any).SelectMany(waitType =>
                        Gen.Choose(2, 10).Select(expectedCount =>
                            new WaitCondition
                            {
                                CorrelationId = correlationId,
                                FlowId = flowId,
                                FlowType = "TestFlow",
                                Type = waitType,
                                ExpectedCount = expectedCount,
                                CompletedCount = 0,
                                Timeout = TimeSpan.FromMinutes(5),
                                CreatedAt = DateTime.UtcNow,
                                Step = 0
                            }))))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成 ForEachProgress
    /// </summary>
    public static Arbitrary<ForEachProgress> ForEachProgressArbitrary()
    {
        return Gen.Choose(5, 100)
            .SelectMany(total =>
                Gen.Choose(0, total - 1).Select(currentIndex =>
                {
                    var completedIndices = Enumerable.Range(0, currentIndex).ToList();
                    return new ForEachProgress
                    {
                        TotalCount = total,
                        CurrentIndex = currentIndex,
                        CompletedIndices = completedIndices,
                        FailedIndices = new List<int>()
                    };
                }))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成 Flow 快照列表（模拟 Flow 执行历史）
    /// </summary>
    public static Arbitrary<List<FlowSnapshot<TestPropertyFlowState>>> FlowSnapshotHistoryArbitrary()
    {
        return FlowIdArbitrary().Generator
            .SelectMany(flowId =>
                TestFlowStateArbitrary().Generator.SelectMany(baseState =>
                    Gen.Choose(2, 10).Select(count =>
                    {
                        var result = new List<FlowSnapshot<TestPropertyFlowState>>();
                        var timestamp = DateTime.UtcNow.AddMinutes(-count);

                        for (int i = 0; i < count; i++)
                        {
                            var status = i == count - 1 ? DslFlowStatus.Completed :
                                        i == 0 ? DslFlowStatus.Pending : DslFlowStatus.Running;

                            result.Add(new FlowSnapshot<TestPropertyFlowState>
                            {
                                FlowId = flowId,
                                State = new TestPropertyFlowState
                                {
                                    OrderId = baseState.OrderId,
                                    Amount = baseState.Amount,
                                    Status = status.ToString(),
                                    Items = baseState.Items
                                },
                                Position = new FlowPosition([i]),
                                Status = status,
                                CreatedAt = timestamp,
                                UpdatedAt = timestamp.AddMinutes(i),
                                Version = i + 1
                            });
                        }
                        return result;
                    })))
            .ToArbitrary();
    }
}

/// <summary>
/// 用于属性测试的测试 Flow 状态
/// </summary>
[MemoryPackable]
public partial class TestPropertyFlowState : BaseFlowState
{
    public string OrderId { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public List<string> Items { get; set; } = new();
    [MemoryPackIgnore]
    public Dictionary<string, object> Metadata { get; set; } = new();
}
