using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Conditional flow scenario tests.
/// Tests complex conditional logic, nested conditions, and dynamic branching.
/// </summary>
public class ConditionalFlowTests
{
    #region Test State

    public class ConditionalState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Value { get; set; }
        public string Category { get; set; } = "";
        public bool Flag1 { get; set; }
        public bool Flag2 { get; set; }
        public List<string> ExecutedBranches { get; set; } = new();
        public string Result { get; set; } = "";
    }

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task If_TrueCondition_ExecutesThenBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("if-true")
            .If(s => s.Value > 10)
                .Then(f => f.Step("then-branch", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("then");
                    state.Result = "greater-than-10";
                    return true;
                }))
                .Else(f => f.Step("else-branch", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("else");
                    state.Result = "not-greater";
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new ConditionalState { FlowId = "if-true-test", Value = 15 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().Contain("then");
        result.State.ExecutedBranches.Should().NotContain("else");
        result.State.Result.Should().Be("greater-than-10");
    }

    [Fact]
    public async Task If_FalseCondition_ExecutesElseBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("if-false")
            .If(s => s.Value > 10)
                .Then(f => f.Step("then-branch", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("then");
                    return true;
                }))
                .Else(f => f.Step("else-branch", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("else");
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new ConditionalState { FlowId = "if-false-test", Value = 5 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().Contain("else");
        result.State.ExecutedBranches.Should().NotContain("then");
    }

    [Fact]
    public async Task ElseIf_MatchesSecondCondition_ExecutesCorrectBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("elseif-chain")
            .If(s => s.Value >= 100)
                .Then(f => f.Step("high", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("high");
                    state.Category = "High";
                    return true;
                }))
            .ElseIf(s => s.Value >= 50)
                .Then(f => f.Step("medium", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("medium");
                    state.Category = "Medium";
                    return true;
                }))
            .ElseIf(s => s.Value >= 10)
                .Then(f => f.Step("low", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("low");
                    state.Category = "Low";
                    return true;
                }))
            .Else(f => f.Step("minimal", async (state, ct) =>
            {
                state.ExecutedBranches.Add("minimal");
                state.Category = "Minimal";
                return true;
            }))
            .EndIf()
            .Build();

        var state = new ConditionalState { FlowId = "elseif-test", Value = 75 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().ContainSingle().Which.Should().Be("medium");
        result.State.Category.Should().Be("Medium");
    }

    [Fact]
    public async Task Switch_MatchesCase_ExecutesCorrectBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("switch-case")
            .Switch(s => s.Category)
                .Case("A", f => f.Step("case-a", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("case-A");
                    state.Result = "Category A processed";
                    return true;
                }))
                .Case("B", f => f.Step("case-b", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("case-B");
                    state.Result = "Category B processed";
                    return true;
                }))
                .Case("C", f => f.Step("case-c", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("case-C");
                    state.Result = "Category C processed";
                    return true;
                }))
                .Default(f => f.Step("default", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("default");
                    state.Result = "Unknown category";
                    return true;
                }))
            .EndSwitch()
            .Build();

        var state = new ConditionalState { FlowId = "switch-test", Category = "B" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().ContainSingle().Which.Should().Be("case-B");
        result.State.Result.Should().Be("Category B processed");
    }

    [Fact]
    public async Task NestedIf_MultiLevel_ExecutesCorrectPath()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("nested-if")
            .If(s => s.Flag1)
                .Then(f => f
                    .Step("flag1-true", async (state, ct) =>
                    {
                        state.ExecutedBranches.Add("flag1-true");
                        return true;
                    })
                    .If(s => s.Flag2)
                        .Then(inner => inner.Step("flag2-true", async (state, ct) =>
                        {
                            state.ExecutedBranches.Add("flag2-true");
                            state.Result = "Both flags true";
                            return true;
                        }))
                        .Else(inner => inner.Step("flag2-false", async (state, ct) =>
                        {
                            state.ExecutedBranches.Add("flag2-false");
                            state.Result = "Only flag1 true";
                            return true;
                        }))
                    .EndIf())
                .Else(f => f.Step("flag1-false", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("flag1-false");
                    state.Result = "Flag1 false";
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new ConditionalState { FlowId = "nested-test", Flag1 = true, Flag2 = true };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().Contain("flag1-true");
        result.State.ExecutedBranches.Should().Contain("flag2-true");
        result.State.Result.Should().Be("Both flags true");
    }

    [Fact]
    public async Task CompoundCondition_MultipleChecks_EvaluatesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("compound-condition")
            .If(s => s.Value > 50 && s.Flag1 && s.Category == "Premium")
                .Then(f => f.Step("all-conditions-met", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("all-met");
                    state.Result = "Premium high-value with flag";
                    return true;
                }))
            .ElseIf(s => s.Value > 50 || s.Flag1)
                .Then(f => f.Step("some-conditions-met", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("some-met");
                    state.Result = "Either high-value or flagged";
                    return true;
                }))
            .Else(f => f.Step("no-conditions-met", async (state, ct) =>
            {
                state.ExecutedBranches.Add("none-met");
                state.Result = "Standard processing";
                return true;
            }))
            .EndIf()
            .Build();

        // Test all conditions met
        var state1 = new ConditionalState { FlowId = "compound-1", Value = 100, Flag1 = true, Category = "Premium" };
        var result1 = await executor.ExecuteAsync(flow, state1);
        result1.State.ExecutedBranches.Should().Contain("all-met");

        // Test partial conditions met
        var state2 = new ConditionalState { FlowId = "compound-2", Value = 75, Flag1 = false, Category = "Standard" };
        var result2 = await executor.ExecuteAsync(flow, state2);
        result2.State.ExecutedBranches.Should().Contain("some-met");

        // Test no conditions met
        var state3 = new ConditionalState { FlowId = "compound-3", Value = 25, Flag1 = false, Category = "Standard" };
        var result3 = await executor.ExecuteAsync(flow, state3);
        result3.State.ExecutedBranches.Should().Contain("none-met");
    }

    [Fact]
    public async Task DynamicBranching_BasedOnState_SelectsCorrectPath()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DynamicState>("dynamic-branching")
            .Step("evaluate", async (state, ct) =>
            {
                state.Score = state.Value * 10 + (state.Flag ? 50 : 0);
                return true;
            })
            .Switch(s => s.Score switch
            {
                >= 200 => "Platinum",
                >= 100 => "Gold",
                >= 50 => "Silver",
                _ => "Bronze"
            })
                .Case("Platinum", f => f.Step("platinum", async (state, ct) =>
                {
                    state.Tier = "Platinum";
                    state.Discount = 0.25m;
                    return true;
                }))
                .Case("Gold", f => f.Step("gold", async (state, ct) =>
                {
                    state.Tier = "Gold";
                    state.Discount = 0.15m;
                    return true;
                }))
                .Case("Silver", f => f.Step("silver", async (state, ct) =>
                {
                    state.Tier = "Silver";
                    state.Discount = 0.10m;
                    return true;
                }))
                .Default(f => f.Step("bronze", async (state, ct) =>
                {
                    state.Tier = "Bronze";
                    state.Discount = 0.05m;
                    return true;
                }))
            .EndSwitch()
            .Build();

        // Test Platinum tier
        var state1 = new DynamicState { FlowId = "dyn-1", Value = 20, Flag = true }; // Score = 250
        var result1 = await executor.ExecuteAsync(flow, state1);
        result1.State.Tier.Should().Be("Platinum");
        result1.State.Discount.Should().Be(0.25m);

        // Test Bronze tier
        var state2 = new DynamicState { FlowId = "dyn-2", Value = 2, Flag = false }; // Score = 20
        var result2 = await executor.ExecuteAsync(flow, state2);
        result2.State.Tier.Should().Be("Bronze");
    }

    [Fact]
    public async Task ConditionalWithSteps_MixedFlow_ExecutesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConditionalState>("mixed-flow")
            .Step("init", async (state, ct) =>
            {
                state.ExecutedBranches.Add("init");
                return true;
            })
            .If(s => s.Value > 50)
                .Then(f => f
                    .Step("high-1", async (state, ct) => { state.ExecutedBranches.Add("high-1"); return true; })
                    .Step("high-2", async (state, ct) => { state.ExecutedBranches.Add("high-2"); return true; }))
            .EndIf()
            .Step("middle", async (state, ct) =>
            {
                state.ExecutedBranches.Add("middle");
                return true;
            })
            .If(s => s.Flag1)
                .Then(f => f.Step("flagged", async (state, ct) =>
                {
                    state.ExecutedBranches.Add("flagged");
                    return true;
                }))
            .EndIf()
            .Step("finalize", async (state, ct) =>
            {
                state.ExecutedBranches.Add("finalize");
                return true;
            })
            .Build();

        var state = new ConditionalState { FlowId = "mixed-test", Value = 75, Flag1 = true };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedBranches.Should().ContainInOrder("init", "high-1", "high-2", "middle", "flagged", "finalize");
    }

    public class DynamicState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Value { get; set; }
        public bool Flag { get; set; }
        public int Score { get; set; }
        public string Tier { get; set; } = "";
        public decimal Discount { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
