using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Approval workflow scenarios with multi-level approvals, escalation, and timeout handling.
/// </summary>
public class ApprovalWorkflowTests
{
    #region Test State

    public class ApprovalState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string RequesterId { get; set; } = "";
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";

        // Approval chain
        public List<ApprovalLevel> ApprovalChain { get; set; } = new();
        public int CurrentLevel { get; set; }

        // Status
        public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
        public string? RejectionReason { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovedBy { get; set; }

        // Escalation
        public bool Escalated { get; set; }
        public int EscalationCount { get; set; }
    }

    public record ApprovalLevel(string ApproverId, string Role, decimal MaxAmount);

    public enum ApprovalStatus { Pending, InReview, Approved, Rejected, Escalated, Expired }

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
    public async Task Approval_LowAmount_SingleApprover()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ApprovalState>("single-approval")
            .Step("validate-request", async (state, ct) =>
            {
                if (state.Amount <= 0) return false;
                state.Status = ApprovalStatus.InReview;
                return true;
            })
            .If(state => state.Amount <= 1000)
                .Then(f => f.Step("manager-approval", async (state, ct) =>
                {
                    // Simulate manager approval
                    state.Status = ApprovalStatus.Approved;
                    state.ApprovedBy = "manager@company.com";
                    state.ApprovedAt = DateTime.UtcNow;
                    return true;
                }))
            .Else(f => f.Step("director-approval", async (state, ct) =>
            {
                state.Status = ApprovalStatus.Approved;
                state.ApprovedBy = "director@company.com";
                state.ApprovedAt = DateTime.UtcNow;
                return true;
            }))
            .EndIf()
            .Build();

        var initialState = new ApprovalState
        {
            FlowId = "approval-001",
            RequestId = "REQ-001",
            Amount = 500m,
            Description = "Office supplies"
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be(ApprovalStatus.Approved);
        result.State.ApprovedBy.Should().Be("manager@company.com");
    }

    [Fact]
    public async Task Approval_HighAmount_MultiLevelApproval()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var approvals = new List<string>();

        var flow = FlowBuilder.Create<ApprovalState>("multi-level-approval")
            .Step("validate", async (state, ct) =>
            {
                state.Status = ApprovalStatus.InReview;
                return true;
            })
            .Step("manager-approval", async (state, ct) =>
            {
                approvals.Add("manager");
                state.CurrentLevel = 1;
                return true;
            })
            .If(state => state.Amount > 5000)
                .Then(f => f.Step("director-approval", async (state, ct) =>
                {
                    approvals.Add("director");
                    state.CurrentLevel = 2;
                    return true;
                }))
            .EndIf()
            .If(state => state.Amount > 20000)
                .Then(f => f.Step("cfo-approval", async (state, ct) =>
                {
                    approvals.Add("cfo");
                    state.CurrentLevel = 3;
                    return true;
                }))
            .EndIf()
            .Step("finalize", async (state, ct) =>
            {
                state.Status = ApprovalStatus.Approved;
                state.ApprovedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        // Test high amount requiring CFO
        var highState = new ApprovalState { FlowId = "high-amount", Amount = 50000m };
        var highResult = await executor.ExecuteAsync(flow, highState);

        // Assert
        highResult.IsSuccess.Should().BeTrue();
        highResult.State.Status.Should().Be(ApprovalStatus.Approved);
        highResult.State.CurrentLevel.Should().Be(3);
        approvals.Should().Contain(new[] { "manager", "director", "cfo" });
    }

    [Fact]
    public async Task Approval_Rejected_StopsWorkflow()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ApprovalState>("rejection-flow")
            .Step("validate", async (state, ct) =>
            {
                state.Status = ApprovalStatus.InReview;
                return true;
            })
            .Step("manager-review", async (state, ct) =>
            {
                // Manager rejects
                state.Status = ApprovalStatus.Rejected;
                state.RejectionReason = "Budget exceeded for Q4";
                return false; // Return false to stop flow
            })
            .Step("process-approval", async (state, ct) =>
            {
                // This should not execute
                state.Status = ApprovalStatus.Approved;
                return true;
            })
            .Build();

        var initialState = new ApprovalState { FlowId = "reject-test", Amount = 10000m };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.Status.Should().Be(ApprovalStatus.Rejected);
        result.State.RejectionReason.Should().Be("Budget exceeded for Q4");
    }

    [Fact]
    public async Task Approval_WithEscalation_EscalatesOnTimeout()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ApprovalState>("escalation-flow")
            .Step("initial-review", async (state, ct) =>
            {
                state.Status = ApprovalStatus.InReview;
                // Simulate timeout scenario
                state.Escalated = true;
                state.EscalationCount = 1;
                return true;
            })
            .If(state => state.Escalated)
                .Then(f => f.Step("escalate-to-senior", async (state, ct) =>
                {
                    state.Status = ApprovalStatus.Escalated;
                    state.ApprovalChain.Add(new ApprovalLevel("senior@company.com", "Senior Manager", 50000m));
                    return true;
                })
                .Step("senior-approval", async (state, ct) =>
                {
                    state.Status = ApprovalStatus.Approved;
                    state.ApprovedBy = "senior@company.com";
                    state.ApprovedAt = DateTime.UtcNow;
                    return true;
                }))
            .EndIf()
            .Build();

        var initialState = new ApprovalState { FlowId = "escalate-test", Amount = 15000m };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Escalated.Should().BeTrue();
        result.State.Status.Should().Be(ApprovalStatus.Approved);
        result.State.ApprovedBy.Should().Be("senior@company.com");
    }

    [Fact]
    public async Task Approval_ParallelReview_WaitsForAllApprovers()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var reviewers = new List<string>();

        var flow = FlowBuilder.Create<ApprovalState>("parallel-review")
            .Step("start-review", async (state, ct) =>
            {
                state.Status = ApprovalStatus.InReview;
                return true;
            })
            .ForEach(
                state => new[] { "legal", "finance", "compliance" },
                (dept, f) => f.Step($"review-{dept}", async (state, ct) =>
                {
                    lock (reviewers) reviewers.Add(dept);
                    return true;
                }))
            .WithParallelism(3)
            .Step("final-approval", async (state, ct) =>
            {
                state.Status = ApprovalStatus.Approved;
                state.ApprovedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var initialState = new ApprovalState { FlowId = "parallel-test", Amount = 100000m };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reviewers.Should().HaveCount(3);
        reviewers.Should().Contain(new[] { "legal", "finance", "compliance" });
        result.State.Status.Should().Be(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task Approval_DynamicApprovalChain_BuildsChainBasedOnAmount()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ApprovalState>("dynamic-chain")
            .Step("build-chain", async (state, ct) =>
            {
                state.ApprovalChain.Clear();

                // Build approval chain based on amount
                state.ApprovalChain.Add(new ApprovalLevel("manager@co.com", "Manager", 5000m));

                if (state.Amount > 5000)
                    state.ApprovalChain.Add(new ApprovalLevel("director@co.com", "Director", 25000m));

                if (state.Amount > 25000)
                    state.ApprovalChain.Add(new ApprovalLevel("vp@co.com", "VP", 100000m));

                if (state.Amount > 100000)
                    state.ApprovalChain.Add(new ApprovalLevel("ceo@co.com", "CEO", decimal.MaxValue));

                return true;
            })
            .Step("process-chain", async (state, ct) =>
            {
                // Process all approvals in chain
                foreach (var level in state.ApprovalChain)
                {
                    state.CurrentLevel++;
                }
                state.Status = ApprovalStatus.Approved;
                state.ApprovedBy = state.ApprovalChain.Last().ApproverId;
                return true;
            })
            .Build();

        // Test different amounts
        var smallState = new ApprovalState { FlowId = "small", Amount = 1000m };
        var smallResult = await executor.ExecuteAsync(flow, smallState);
        smallResult.State.ApprovalChain.Should().HaveCount(1);

        var mediumState = new ApprovalState { FlowId = "medium", Amount = 15000m };
        var mediumResult = await executor.ExecuteAsync(flow, mediumState);
        mediumResult.State.ApprovalChain.Should().HaveCount(2);

        var largeState = new ApprovalState { FlowId = "large", Amount = 150000m };
        var largeResult = await executor.ExecuteAsync(flow, largeState);
        largeResult.State.ApprovalChain.Should().HaveCount(4);
        largeResult.State.ApprovedBy.Should().Be("ceo@co.com");
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
