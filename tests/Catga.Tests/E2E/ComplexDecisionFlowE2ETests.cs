using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.E2E;

/// <summary>
/// Complex decision flow E2E tests covering multi-level conditional logic.
/// Tests scenarios with multiple intersecting conditions and decision branches.
/// </summary>
public class ComplexDecisionFlowE2ETests
{
    private readonly ITestOutputHelper _output;

    public ComplexDecisionFlowE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Simple Decision Scenarios

    [Fact]
    public async Task E2E_SimpleApproval_SingleCondition()
    {
        // Arrange - Simple approval based on single condition
        var services = new ServiceCollection();
        var mediator = SetupSimpleApprovalMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleApprovalState, SimpleApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleApprovalState, SimpleApprovalFlow>>();

        var request = new SimpleApprovalState
        {
            FlowId = "simple-001",
            RequestId = "REQ-001",
            Amount = 500.00m,
            RequesterLevel = "Manager"
        };

        // Act
        var result = await executor!.RunAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Approved.Should().BeTrue();
        result.State.ApprovalPath.Should().Be("DirectApproval");

        _output.WriteLine($"✓ Simple approval: {result.State.ApprovalPath}");
    }

    [Fact]
    public async Task E2E_SimpleRejection_SingleCondition()
    {
        // Arrange - Simple rejection based on single condition
        var services = new ServiceCollection();
        var mediator = SetupSimpleApprovalMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleApprovalState, SimpleApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleApprovalState, SimpleApprovalFlow>>();

        var request = new SimpleApprovalState
        {
            FlowId = "simple-002",
            RequestId = "REQ-002",
            Amount = 50000.00m,
            RequesterLevel = "Employee"
        };

        // Act
        var result = await executor!.RunAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Approved.Should().BeFalse();
        result.State.ApprovalPath.Should().Be("Rejected");

        _output.WriteLine($"✓ Simple rejection: {result.State.ApprovalPath}");
    }

    #endregion

    #region Complex Multi-Condition Scenarios

    [Fact]
    public async Task E2E_MultiConditionApproval_AllConditionsMet()
    {
        // Arrange - Approval with multiple intersecting conditions (all met)
        var services = new ServiceCollection();
        var mediator = SetupMultiConditionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MultiConditionApprovalState, MultiConditionApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MultiConditionApprovalState, MultiConditionApprovalFlow>>();

        var request = new MultiConditionApprovalState
        {
            FlowId = "multi-001",
            RequestId = "REQ-MULTI-001",
            Amount = 10000.00m,
            RequesterLevel = "Director",
            CreditScore = 750,
            EmploymentYears = 5,
            PreviousDefaults = 0,
            DepartmentBudget = 50000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CreditCheckPassed.Should().BeTrue();
        result.State.EmploymentCheckPassed.Should().BeTrue();
        result.State.BudgetCheckPassed.Should().BeTrue();
        result.State.Approved.Should().BeTrue();
        result.State.ApprovalPath.Should().Be("FastTrack");

        _output.WriteLine($"✓ Multi-condition approval (all met) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ApprovalPath}");
    }

    [Fact]
    public async Task E2E_MultiConditionApproval_PartialConditionsMet()
    {
        // Arrange - Approval with multiple conditions (some met, some not)
        var services = new ServiceCollection();
        var mediator = SetupMultiConditionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MultiConditionApprovalState, MultiConditionApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MultiConditionApprovalState, MultiConditionApprovalFlow>>();

        var request = new MultiConditionApprovalState
        {
            FlowId = "multi-002",
            RequestId = "REQ-MULTI-002",
            Amount = 10000.00m,
            RequesterLevel = "Manager",
            CreditScore = 650,
            EmploymentYears = 2,
            PreviousDefaults = 1,
            DepartmentBudget = 50000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CreditCheckPassed.Should().BeFalse();
        result.State.EmploymentCheckPassed.Should().BeFalse();
        result.State.BudgetCheckPassed.Should().BeTrue();
        result.State.Approved.Should().BeFalse();
        result.State.ApprovalPath.Should().Be("ManualReview");

        _output.WriteLine($"✓ Multi-condition approval (partial) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ApprovalPath}");
    }

    [Fact]
    public async Task E2E_MultiConditionApproval_NoConditionsMet()
    {
        // Arrange - Approval with multiple conditions (none met)
        var services = new ServiceCollection();
        var mediator = SetupMultiConditionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MultiConditionApprovalState, MultiConditionApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MultiConditionApprovalState, MultiConditionApprovalFlow>>();

        var request = new MultiConditionApprovalState
        {
            FlowId = "multi-003",
            RequestId = "REQ-MULTI-003",
            Amount = 50000.00m,
            RequesterLevel = "Employee",
            CreditScore = 500,
            EmploymentYears = 0,
            PreviousDefaults = 5,
            DepartmentBudget = 1000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CreditCheckPassed.Should().BeFalse();
        result.State.EmploymentCheckPassed.Should().BeFalse();
        result.State.BudgetCheckPassed.Should().BeFalse();
        result.State.Approved.Should().BeFalse();
        result.State.ApprovalPath.Should().Be("Rejected");

        _output.WriteLine($"✓ Multi-condition approval (none met) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ApprovalPath}");
    }

    #endregion

    #region Nested Decision Scenarios

    [Fact]
    public async Task E2E_NestedDecisions_PremiumPath()
    {
        // Arrange - Nested decisions leading to premium path
        var services = new ServiceCollection();
        var mediator = SetupNestedDecisionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<NestedDecisionState, NestedDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<NestedDecisionState, NestedDecisionFlow>>();

        var request = new NestedDecisionState
        {
            FlowId = "nested-001",
            CustomerId = "CUST-PREMIUM-001",
            OrderAmount = 5000.00m,
            CustomerTier = "Gold",
            OrderFrequency = 12,
            TotalSpent = 50000.00m,
            HasLoyaltyProgram = true
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IsHighValue.Should().BeTrue();
        result.State.IsLoyalCustomer.Should().BeTrue();
        result.State.ProcessingPath.Should().Be("PremiumProcessing");
        result.State.DiscountApplied.Should().BeGreaterThan(0);
        result.State.PriorityHandling.Should().BeTrue();

        _output.WriteLine($"✓ Nested decisions (premium) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}");
    }

    [Fact]
    public async Task E2E_NestedDecisions_StandardPath()
    {
        // Arrange - Nested decisions leading to standard path
        var services = new ServiceCollection();
        var mediator = SetupNestedDecisionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<NestedDecisionState, NestedDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<NestedDecisionState, NestedDecisionFlow>>();

        var request = new NestedDecisionState
        {
            FlowId = "nested-002",
            CustomerId = "CUST-STANDARD-001",
            OrderAmount = 100.00m,
            CustomerTier = "Silver",
            OrderFrequency = 2,
            TotalSpent = 500.00m,
            HasLoyaltyProgram = false
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IsHighValue.Should().BeFalse();
        result.State.IsLoyalCustomer.Should().BeFalse();
        result.State.ProcessingPath.Should().Be("StandardProcessing");
        result.State.DiscountApplied.Should().Be(0);
        result.State.PriorityHandling.Should().BeFalse();

        _output.WriteLine($"✓ Nested decisions (standard) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}");
    }

    [Fact]
    public async Task E2E_NestedDecisions_EconomyPath()
    {
        // Arrange - Nested decisions leading to economy path
        var services = new ServiceCollection();
        var mediator = SetupNestedDecisionMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<NestedDecisionState, NestedDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<NestedDecisionState, NestedDecisionFlow>>();

        var request = new NestedDecisionState
        {
            FlowId = "nested-003",
            CustomerId = "CUST-ECONOMY-001",
            OrderAmount = 20.00m,
            CustomerTier = "Bronze",
            OrderFrequency = 1,
            TotalSpent = 50.00m,
            HasLoyaltyProgram = false
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IsHighValue.Should().BeFalse();
        result.State.IsLoyalCustomer.Should().BeFalse();
        result.State.ProcessingPath.Should().Be("EconomyProcessing");
        result.State.DiscountApplied.Should().Be(0);
        result.State.PriorityHandling.Should().BeFalse();

        _output.WriteLine($"✓ Nested decisions (economy) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}");
    }

    #endregion

    #region Cross-Cutting Decision Scenarios

    [Fact]
    public async Task E2E_CrossCuttingDecisions_HighRiskHighValue()
    {
        // Arrange - Cross-cutting decisions: high risk + high value
        var services = new ServiceCollection();
        var mediator = SetupCrossCuttingMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<CrossCuttingDecisionState, CrossCuttingDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CrossCuttingDecisionState, CrossCuttingDecisionFlow>>();

        var request = new CrossCuttingDecisionState
        {
            FlowId = "cross-001",
            TransactionId = "TXN-001",
            Amount = 100000.00m,
            RiskScore = 0.85m,
            IsNewCustomer = true,
            HasFraudHistory = true,
            IsHighValue = true,
            IsInternational = true
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.RequiresManualReview.Should().BeTrue();
        result.State.FraudCheckRequired.Should().BeTrue();
        result.State.ComplianceCheckRequired.Should().BeTrue();
        result.State.ProcessingPath.Should().Be("HighRiskHighValue");
        result.State.EscalationLevel.Should().Be(3);

        _output.WriteLine($"✓ Cross-cutting (high risk + high value) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}, Level {result.State.EscalationLevel}");
    }

    [Fact]
    public async Task E2E_CrossCuttingDecisions_LowRiskLowValue()
    {
        // Arrange - Cross-cutting decisions: low risk + low value
        var services = new ServiceCollection();
        var mediator = SetupCrossCuttingMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<CrossCuttingDecisionState, CrossCuttingDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CrossCuttingDecisionState, CrossCuttingDecisionFlow>>();

        var request = new CrossCuttingDecisionState
        {
            FlowId = "cross-002",
            TransactionId = "TXN-002",
            Amount = 50.00m,
            RiskScore = 0.05m,
            IsNewCustomer = false,
            HasFraudHistory = false,
            IsHighValue = false,
            IsInternational = false
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.RequiresManualReview.Should().BeFalse();
        result.State.FraudCheckRequired.Should().BeFalse();
        result.State.ComplianceCheckRequired.Should().BeFalse();
        result.State.ProcessingPath.Should().Be("AutoApproved");
        result.State.EscalationLevel.Should().Be(0);

        _output.WriteLine($"✓ Cross-cutting (low risk + low value) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}, Level {result.State.EscalationLevel}");
    }

    [Fact]
    public async Task E2E_CrossCuttingDecisions_MixedRiskValue()
    {
        // Arrange - Cross-cutting decisions: mixed risk and value
        var services = new ServiceCollection();
        var mediator = SetupCrossCuttingMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<CrossCuttingDecisionState, CrossCuttingDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CrossCuttingDecisionState, CrossCuttingDecisionFlow>>();

        var request = new CrossCuttingDecisionState
        {
            FlowId = "cross-003",
            TransactionId = "TXN-003",
            Amount = 5000.00m,
            RiskScore = 0.45m,
            IsNewCustomer = true,
            HasFraudHistory = false,
            IsHighValue = true,
            IsInternational = false
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.RequiresManualReview.Should().BeTrue();
        result.State.FraudCheckRequired.Should().BeFalse();
        result.State.ComplianceCheckRequired.Should().BeFalse();
        result.State.ProcessingPath.Should().Be("ManualReview");
        result.State.EscalationLevel.Should().Be(1);

        _output.WriteLine($"✓ Cross-cutting (mixed) in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}, Level {result.State.EscalationLevel}");
    }

    #endregion

    #region Complex Multi-Branch Scenarios

    [Fact]
    public async Task E2E_MultiBranchDecision_ComplexScenario()
    {
        // Arrange - Complex multi-branch decision with multiple conditions
        var services = new ServiceCollection();
        var mediator = SetupMultiBranchMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MultiBranchDecisionState, MultiBranchDecisionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MultiBranchDecisionState, MultiBranchDecisionFlow>>();

        var request = new MultiBranchDecisionState
        {
            FlowId = "multibranch-001",
            RequestId = "REQ-MB-001",
            Amount = 15000.00m,
            Priority = "High",
            Category = "Hardware",
            Urgency = "Critical",
            BudgetAvailable = 20000.00m,
            ApprovalChain = new List<string> { "Manager", "Director" }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(request);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessingPath.Should().NotBeNullOrEmpty();
        result.State.ApprovalStepsRequired.Should().BeGreaterThan(0);
        result.State.EstimatedProcessingTime.Should().BeGreaterThan(0);

        _output.WriteLine($"✓ Multi-branch decision in {stopwatch.ElapsedMilliseconds}ms: {result.State.ProcessingPath}");
        _output.WriteLine($"  Approval Steps: {result.State.ApprovalStepsRequired}");
        _output.WriteLine($"  Est. Time: {result.State.EstimatedProcessingTime} hours");
    }

    #endregion

    #region Helper Methods

    private ICatgaMediator SetupSimpleApprovalMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        return mediator;
    }

    private ICatgaMediator SetupMultiConditionMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        return mediator;
    }

    private ICatgaMediator SetupNestedDecisionMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        return mediator;
    }

    private ICatgaMediator SetupCrossCuttingMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        return mediator;
    }

    private ICatgaMediator SetupMultiBranchMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        return mediator;
    }

    #endregion
}

// ========== Flow Configurations ==========

public class SimpleApprovalFlow : FlowConfig<SimpleApprovalState>
{
    protected override void Configure(IFlowBuilder<SimpleApprovalState> flow)
    {
        flow.Name("simple-approval");

        flow.If(s => s.Amount <= 1000 && s.RequesterLevel != "Employee")
            .Then(f => f.Step("approve", s =>
            {
                s.Approved = true;
                s.ApprovalPath = "DirectApproval";
            }))
            .Else(f => f.Step("reject", s =>
            {
                s.Approved = false;
                s.ApprovalPath = "Rejected";
            }))
            .EndIf();
    }
}

public class MultiConditionApprovalFlow : FlowConfig<MultiConditionApprovalState>
{
    protected override void Configure(IFlowBuilder<MultiConditionApprovalState> flow)
    {
        flow.Name("multi-condition-approval");

        flow.Step("check-credit", s => s.CreditCheckPassed = s.CreditScore >= 700);
        flow.Step("check-employment", s => s.EmploymentCheckPassed = s.EmploymentYears >= 3 && s.PreviousDefaults == 0);
        flow.Step("check-budget", s => s.BudgetCheckPassed = s.Amount <= s.DepartmentBudget);

        flow.If(s => s.CreditCheckPassed && s.EmploymentCheckPassed && s.BudgetCheckPassed)
            .Then(f => f.Step("fast-track", s =>
            {
                s.Approved = true;
                s.ApprovalPath = "FastTrack";
            }))
            .ElseIf(s => s.CreditCheckPassed || s.EmploymentCheckPassed || s.BudgetCheckPassed)
            .Then(f => f.Step("manual-review", s =>
            {
                s.Approved = false;
                s.ApprovalPath = "ManualReview";
            }))
            .Else(f => f.Step("reject", s =>
            {
                s.Approved = false;
                s.ApprovalPath = "Rejected";
            }))
            .EndIf();
    }
}

public class NestedDecisionFlow : FlowConfig<NestedDecisionState>
{
    protected override void Configure(IFlowBuilder<NestedDecisionState> flow)
    {
        flow.Name("nested-decision");

        flow.Step("check-high-value", s => s.IsHighValue = s.OrderAmount >= 1000 && s.TotalSpent >= 10000);
        flow.Step("check-loyalty", s => s.IsLoyalCustomer = s.OrderFrequency >= 6 && s.HasLoyaltyProgram);

        flow.If(s => s.IsHighValue)
            .Then(f =>
            {
                f.If(s => s.IsLoyalCustomer)
                    .Then(f2 => f2.Step("premium-path", s =>
                    {
                        s.ProcessingPath = "PremiumProcessing";
                        s.DiscountApplied = s.OrderAmount * 0.15m;
                        s.PriorityHandling = true;
                    }))
                    .Else(f2 => f2.Step("standard-path", s =>
                    {
                        s.ProcessingPath = "StandardProcessing";
                        s.DiscountApplied = s.OrderAmount * 0.05m;
                        s.PriorityHandling = false;
                    }))
                    .EndIf();
            })
            .Else(f => f.Step("economy-path", s =>
            {
                s.ProcessingPath = "EconomyProcessing";
                s.DiscountApplied = 0;
                s.PriorityHandling = false;
            }))
            .EndIf();
    }
}

public class CrossCuttingDecisionFlow : FlowConfig<CrossCuttingDecisionState>
{
    protected override void Configure(IFlowBuilder<CrossCuttingDecisionState> flow)
    {
        flow.Name("cross-cutting-decision");

        flow.Step("assess-risk", s =>
        {
            s.RequiresManualReview = s.RiskScore > 0.5 || s.IsNewCustomer;
            s.FraudCheckRequired = s.HasFraudHistory || s.RiskScore > 0.7;
            s.ComplianceCheckRequired = s.IsInternational || s.Amount > 50000;
        });

        flow.If(s => s.RiskScore > 0.7 && s.IsHighValue)
            .Then(f => f.Step("high-risk-high-value", s =>
            {
                s.ProcessingPath = "HighRiskHighValue";
                s.EscalationLevel = 3;
            }))
            .ElseIf(s => s.RiskScore > 0.5 && s.IsHighValue)
            .Then(f => f.Step("medium-risk-high-value", s =>
            {
                s.ProcessingPath = "ManualReview";
                s.EscalationLevel = 2;
            }))
            .ElseIf(s => s.RiskScore > 0.5)
            .Then(f => f.Step("medium-risk", s =>
            {
                s.ProcessingPath = "ManualReview";
                s.EscalationLevel = 1;
            }))
            .Else(f => f.Step("auto-approved", s =>
            {
                s.ProcessingPath = "AutoApproved";
                s.EscalationLevel = 0;
            }))
            .EndIf();
    }
}

public class MultiBranchDecisionFlow : FlowConfig<MultiBranchDecisionState>
{
    protected override void Configure(IFlowBuilder<MultiBranchDecisionState> flow)
    {
        flow.Name("multi-branch-decision");

        flow.Switch(s => s.Priority)
            .Case("Critical", f => f.Step("critical-path", s =>
            {
                s.ProcessingPath = "CriticalPath";
                s.ApprovalStepsRequired = 3;
                s.EstimatedProcessingTime = 1;
            }))
            .Case("High", f => f.Step("high-path", s =>
            {
                s.ProcessingPath = "HighPath";
                s.ApprovalStepsRequired = 2;
                s.EstimatedProcessingTime = 4;
            }))
            .Case("Medium", f => f.Step("medium-path", s =>
            {
                s.ProcessingPath = "MediumPath";
                s.ApprovalStepsRequired = 1;
                s.EstimatedProcessingTime = 8;
            }))
            .Default(f => f.Step("low-path", s =>
            {
                s.ProcessingPath = "LowPath";
                s.ApprovalStepsRequired = 0;
                s.EstimatedProcessingTime = 24;
            }))
            .EndSwitch();
    }
}

// ========== States ==========

public class SimpleApprovalState : IFlowState
{
    public string? FlowId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RequesterLevel { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public string ApprovalPath { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class MultiConditionApprovalState : IFlowState
{
    public string? FlowId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RequesterLevel { get; set; } = string.Empty;
    public int CreditScore { get; set; }
    public int EmploymentYears { get; set; }
    public int PreviousDefaults { get; set; }
    public decimal DepartmentBudget { get; set; }
    public bool CreditCheckPassed { get; set; }
    public bool EmploymentCheckPassed { get; set; }
    public bool BudgetCheckPassed { get; set; }
    public bool Approved { get; set; }
    public string ApprovalPath { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class NestedDecisionState : IFlowState
{
    public string? FlowId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal OrderAmount { get; set; }
    public string CustomerTier { get; set; } = string.Empty;
    public int OrderFrequency { get; set; }
    public decimal TotalSpent { get; set; }
    public bool HasLoyaltyProgram { get; set; }
    public bool IsHighValue { get; set; }
    public bool IsLoyalCustomer { get; set; }
    public string ProcessingPath { get; set; } = string.Empty;
    public decimal DiscountApplied { get; set; }
    public bool PriorityHandling { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class CrossCuttingDecisionState : IFlowState
{
    public string? FlowId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RiskScore { get; set; }
    public bool IsNewCustomer { get; set; }
    public bool HasFraudHistory { get; set; }
    public bool IsHighValue { get; set; }
    public bool IsInternational { get; set; }
    public bool RequiresManualReview { get; set; }
    public bool FraudCheckRequired { get; set; }
    public bool ComplianceCheckRequired { get; set; }
    public string ProcessingPath { get; set; } = string.Empty;
    public int EscalationLevel { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class MultiBranchDecisionState : IFlowState
{
    public string? FlowId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Urgency { get; set; } = string.Empty;
    public decimal BudgetAvailable { get; set; }
    public List<string> ApprovalChain { get; set; } = new();
    public string ProcessingPath { get; set; } = string.Empty;
    public int ApprovalStepsRequired { get; set; }
    public int EstimatedProcessingTime { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
