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
/// Advanced workflow E2E tests covering complex multi-step scenarios.
/// Tests sophisticated business logic with branching, loops, and coordination.
/// </summary>
public class AdvancedWorkflowE2ETests
{
    private readonly ITestOutputHelper _output;

    public AdvancedWorkflowE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task E2E_LoanApprovalWorkflow_WithMultipleLevels()
    {
        // Arrange - Loan approval with credit check, income verification, and approval levels
        var services = new ServiceCollection();
        var mediator = SetupLoanApprovalMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<LoanApplicationState, LoanApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<LoanApplicationState, LoanApprovalFlow>>();

        var application = new LoanApplicationState
        {
            FlowId = "loan-001",
            ApplicationId = "APP-LOAN-001",
            ApplicantId = "APPL-001",
            LoanAmount = 250000.00m,
            LoanTerm = 30,
            Purpose = "HomePurchase",
            CreditScore = 750,
            AnnualIncome = 120000.00m,
            EmploymentStatus = "Employed",
            ExistingDebts = 50000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(application);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CreditCheckCompleted.Should().BeTrue();
        result.State.CreditApproved.Should().BeTrue();
        result.State.IncomeVerified.Should().BeTrue();
        result.State.DebtToIncomeRatioAcceptable.Should().BeTrue();
        result.State.ApprovalLevel.Should().BeGreaterThan(0);
        result.State.LoanApproved.Should().BeTrue();
        result.State.OfferGenerated.Should().BeTrue();

        _output.WriteLine($"✓ Loan application processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Loan Amount: ${result.State.LoanAmount:F2}");
        _output.WriteLine($"  Credit Score: {result.State.CreditScore}");
        _output.WriteLine($"  Approval Level: {result.State.ApprovalLevel}");
        _output.WriteLine($"  Interest Rate: {result.State.InterestRate:P2}");
    }

    [Fact]
    public async Task E2E_InsuranceClaimProcessing_WithValidationAndApproval()
    {
        // Arrange - Insurance claim with validation, assessment, and approval
        var services = new ServiceCollection();
        var mediator = SetupInsuranceClaimMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<InsuranceClaimState, InsuranceClaimFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<InsuranceClaimState, InsuranceClaimFlow>>();

        var claim = new InsuranceClaimState
        {
            FlowId = "claim-001",
            ClaimId = "CLM-001",
            PolicyId = "POL-12345",
            ClaimType = "AutoAccident",
            ClaimAmount = 15000.00m,
            DateOfLoss = DateTime.UtcNow.AddDays(-5),
            IncidentDescription = "Car accident on highway",
            Documents = new List<string> { "police-report.pdf", "photos.zip", "repair-estimate.pdf" }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(claim);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ClaimValidated.Should().BeTrue();
        result.State.DocumentsVerified.Should().BeTrue();
        result.State.AssessmentCompleted.Should().BeTrue();
        result.State.AssessedAmount.Should().BeGreaterThan(0);
        result.State.ClaimApproved.Should().BeTrue();
        result.State.PaymentProcessed.Should().BeTrue();

        _output.WriteLine($"✓ Insurance claim processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Claim Amount: ${result.State.ClaimAmount:F2}");
        _output.WriteLine($"  Assessed Amount: ${result.State.AssessedAmount:F2}");
        _output.WriteLine($"  Approval Status: {(result.State.ClaimApproved ? "Approved" : "Denied")}");
    }

    [Fact]
    public async Task E2E_SupplyChainOptimization_WithMultipleWarehouses()
    {
        // Arrange - Supply chain with multi-warehouse coordination
        var services = new ServiceCollection();
        var mediator = SetupSupplyChainMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SupplyChainState, SupplyChainOptimizationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SupplyChainState, SupplyChainOptimizationFlow>>();

        var supplyChain = new SupplyChainState
        {
            FlowId = "supply-001",
            OrderId = "ORD-SUPPLY-001",
            RequestedItems = new List<SupplyItem>
            {
                new() { ProductId = "PROD-001", Quantity = 500, Priority = "High" },
                new() { ProductId = "PROD-002", Quantity = 300, Priority = "Medium" },
                new() { ProductId = "PROD-003", Quantity = 200, Priority = "Low" }
            },
            Warehouses = new List<string> { "WH-EAST", "WH-CENTRAL", "WH-WEST" },
            DeliveryDeadline = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(supplyChain);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.InventoryChecked.Should().BeTrue();
        result.State.OptimalRoutesCalculated.Should().BeTrue();
        result.State.ShipmentsScheduled.Should().BeGreaterThan(0);
        result.State.CostOptimized.Should().BeTrue();
        result.State.AllItemsAllocated.Should().BeTrue();

        _output.WriteLine($"✓ Supply chain optimized in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Items Requested: {result.State.RequestedItems.Count}");
        _output.WriteLine($"  Shipments Scheduled: {result.State.ShipmentsScheduled}");
        _output.WriteLine($"  Total Cost: ${result.State.TotalCost:F2}");
    }

    [Fact]
    public async Task E2E_HROnboarding_WithMultipleApprovals()
    {
        // Arrange - HR onboarding with multiple approval steps
        var services = new ServiceCollection();
        var mediator = SetupHROnboardingMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<HROnboardingState, HROnboardingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<HROnboardingState, HROnboardingFlow>>();

        var onboarding = new HROnboardingState
        {
            FlowId = "onboard-001",
            EmployeeId = "EMP-001",
            FullName = "John Doe",
            Position = "Senior Engineer",
            Department = "Engineering",
            StartDate = DateTime.UtcNow.AddDays(14),
            ManagerId = "MGR-001",
            Salary = 150000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(onboarding);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.BackgroundCheckCompleted.Should().BeTrue();
        result.State.ManagerApproved.Should().BeTrue();
        result.State.HRApproved.Should().BeTrue();
        result.State.EquipmentOrdered.Should().BeTrue();
        result.State.AccountsCreated.Should().BeTrue();
        result.State.OnboardingCompleted.Should().BeTrue();

        _output.WriteLine($"✓ HR onboarding completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Employee: {result.State.FullName}");
        _output.WriteLine($"  Position: {result.State.Position}");
        _output.WriteLine($"  Start Date: {result.State.StartDate:yyyy-MM-dd}");
    }

    [Fact]
    public async Task E2E_DocumentApprovalWorkflow_WithSignatures()
    {
        // Arrange - Document approval with multiple signatories
        var services = new ServiceCollection();
        var mediator = SetupDocumentApprovalMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<DocumentApprovalState, DocumentApprovalFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<DocumentApprovalState, DocumentApprovalFlow>>();

        var document = new DocumentApprovalState
        {
            FlowId = "doc-001",
            DocumentId = "DOC-CONTRACT-001",
            DocumentType = "Contract",
            Title = "Service Agreement",
            Signatories = new List<Signatory>
            {
                new() { Name = "Alice Manager", Role = "Manager", Email = "alice@company.com" },
                new() { Name = "Bob Director", Role = "Director", Email = "bob@company.com" },
                new() { Name = "Carol Legal", Role = "Legal", Email = "carol@company.com" }
            },
            CreatedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(5)
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(document);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.SignaturesCollected.Should().Be(3);
        result.State.AllSignaturesReceived.Should().BeTrue();
        result.State.DocumentFinalized.Should().BeTrue();
        result.State.ArchiveStored.Should().BeTrue();

        _output.WriteLine($"✓ Document approval completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Document: {result.State.Title}");
        _output.WriteLine($"  Signatories: {result.State.Signatories.Count}");
        _output.WriteLine($"  Signatures Collected: {result.State.SignaturesCollected}");
    }

    [Fact]
    public async Task E2E_ProjectManagement_WithTaskDependencies()
    {
        // Arrange - Project with task dependencies and milestones
        var services = new ServiceCollection();
        var mediator = SetupProjectMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ProjectManagementState, ProjectManagementFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ProjectManagementState, ProjectManagementFlow>>();

        var project = new ProjectManagementState
        {
            FlowId = "project-001",
            ProjectId = "PROJ-001",
            ProjectName = "Website Redesign",
            Tasks = new List<ProjectTask>
            {
                new() { TaskId = "T1", Name = "Design", Duration = 10, Dependencies = new List<string>() },
                new() { TaskId = "T2", Name = "Development", Duration = 20, Dependencies = new List<string> { "T1" } },
                new() { TaskId = "T3", Name = "Testing", Duration = 10, Dependencies = new List<string> { "T2" } },
                new() { TaskId = "T4", Name = "Deployment", Duration = 5, Dependencies = new List<string> { "T3" } }
            },
            StartDate = DateTime.UtcNow,
            Budget = 100000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(project);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ScheduleCreated.Should().BeTrue();
        result.State.CriticalPathIdentified.Should().BeTrue();
        result.State.ResourcesAllocated.Should().BeTrue();
        result.State.ProjectStarted.Should().BeTrue();

        _output.WriteLine($"✓ Project management setup completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Project: {result.State.ProjectName}");
        _output.WriteLine($"  Tasks: {result.State.Tasks.Count}");
        _output.WriteLine($"  Total Duration: {result.State.TotalDuration} days");
        _output.WriteLine($"  Budget: ${result.State.Budget:F2}");
    }

    #region Helper Methods

    private ICatgaMediator SetupLoanApprovalMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<PerformCreditCheckCommand, bool>(Arg.Any<PerformCreditCheckCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<VerifyIncomeCommand, bool>(Arg.Any<VerifyIncomeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<CalculateDebtToIncomeCommand, decimal>(Arg.Any<CalculateDebtToIncomeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(0.35m)));

        mediator.SendAsync<DetermineLoanApprovalCommand, LoanApprovalResult>(Arg.Any<DetermineLoanApprovalCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var result = new LoanApprovalResult
                {
                    Approved = true,
                    ApprovalLevel = 2,
                    InterestRate = 0.045m,
                    MonthlyPayment = 1266.71m
                };
                return new ValueTask<CatgaResult<LoanApprovalResult>>(CatgaResult<LoanApprovalResult>.Success(result));
            });

        return mediator;
    }

    private ICatgaMediator SetupInsuranceClaimMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ValidateClaimCommand, bool>(Arg.Any<ValidateClaimCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<VerifyDocumentsCommand, bool>(Arg.Any<VerifyDocumentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<AssessClaimCommand, decimal>(Arg.Any<AssessClaimCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(14500.00m)));

        mediator.SendAsync<ApproveClaimCommand, bool>(Arg.Any<ApproveClaimCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupSupplyChainMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<CheckInventoryCommand, List<InventoryStatus>>(Arg.Any<CheckInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var statuses = new List<InventoryStatus>
                {
                    new() { ProductId = "PROD-001", Available = 500, Location = "WH-EAST" },
                    new() { ProductId = "PROD-002", Available = 300, Location = "WH-CENTRAL" },
                    new() { ProductId = "PROD-003", Available = 200, Location = "WH-WEST" }
                };
                return new ValueTask<CatgaResult<List<InventoryStatus>>>(CatgaResult<List<InventoryStatus>>.Success(statuses));
            });

        mediator.SendAsync<OptimizeRoutesCommand, decimal>(Arg.Any<OptimizeRoutesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(5000.00m)));

        return mediator;
    }

    private ICatgaMediator SetupHROnboardingMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<PerformBackgroundCheckCommand, bool>(Arg.Any<PerformBackgroundCheckCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<RequestManagerApprovalCommand, bool>(Arg.Any<RequestManagerApprovalCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<OrderEquipmentCommand, bool>(Arg.Any<OrderEquipmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<CreateAccountsCommand, bool>(Arg.Any<CreateAccountsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupDocumentApprovalMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<RequestSignatureCommand, bool>(Arg.Any<RequestSignatureCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<FinalizeDocumentCommand, bool>(Arg.Any<FinalizeDocumentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupProjectMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<CreateScheduleCommand, bool>(Arg.Any<CreateScheduleCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<IdentifyCriticalPathCommand, int>(Arg.Any<IdentifyCriticalPathCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(45)));

        mediator.SendAsync<AllocateResourcesCommand, bool>(Arg.Any<AllocateResourcesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    #endregion
}

// ========== Flow Configurations ==========

public class LoanApprovalFlow : FlowConfig<LoanApplicationState>
{
    protected override void Configure(IFlowBuilder<LoanApplicationState> flow)
    {
        flow.Name("loan-approval");

        flow.Send(s => new PerformCreditCheckCommand { CreditScore = s.CreditScore })
            .Into((s, r) => s.CreditCheckCompleted = r.Value && s.CreditScore >= 700);

        flow.If(s => s.CreditCheckCompleted)
            .Then(f => f.Send(s => new VerifyIncomeCommand { AnnualIncome = s.AnnualIncome, EmploymentStatus = s.EmploymentStatus })
                .Into((s, r) => s.IncomeVerified = r.Value))
            .EndIf();

        flow.Send(s => new CalculateDebtToIncomeCommand { Income = s.AnnualIncome, Debt = s.ExistingDebts })
            .Into((s, r) => s.DebtToIncomeRatioAcceptable = r.Value < 0.43m);

        flow.Send(s => new DetermineLoanApprovalCommand { Amount = s.LoanAmount, CreditScore = s.CreditScore })
            .Into((s, r) =>
            {
                if (r.IsSuccess && r.Value != null)
                {
                    s.LoanApproved = r.Value.Approved;
                    s.ApprovalLevel = r.Value.ApprovalLevel;
                    s.InterestRate = r.Value.InterestRate;
                    s.MonthlyPayment = r.Value.MonthlyPayment;
                }
            });

        flow.Step("generate-offer", s => s.OfferGenerated = true);
    }
}

public class InsuranceClaimFlow : FlowConfig<InsuranceClaimState>
{
    protected override void Configure(IFlowBuilder<InsuranceClaimState> flow)
    {
        flow.Name("insurance-claim");

        flow.Send(s => new ValidateClaimCommand { ClaimId = s.ClaimId, PolicyId = s.PolicyId })
            .Into((s, r) => s.ClaimValidated = r.Value);

        flow.Send(s => new VerifyDocumentsCommand { Documents = s.Documents })
            .Into((s, r) => s.DocumentsVerified = r.Value);

        flow.Send(s => new AssessClaimCommand { ClaimAmount = s.ClaimAmount, ClaimType = s.ClaimType })
            .Into((s, r) => s.AssessedAmount = r.Value);

        flow.Send(s => new ApproveClaimCommand { ClaimId = s.ClaimId, AssessedAmount = s.AssessedAmount })
            .Into((s, r) => s.ClaimApproved = r.Value);

        flow.Step("process-payment", s => s.PaymentProcessed = true);
    }
}

public class SupplyChainOptimizationFlow : FlowConfig<SupplyChainState>
{
    protected override void Configure(IFlowBuilder<SupplyChainState> flow)
    {
        flow.Name("supply-chain");

        flow.Send(s => new CheckInventoryCommand { Items = s.RequestedItems })
            .Into((s, r) => s.InventoryChecked = true);

        flow.Send(s => new OptimizeRoutesCommand { Warehouses = s.Warehouses, Items = s.RequestedItems })
            .Into((s, r) => s.TotalCost = r.Value);

        flow.Step("calculate-optimal-routes", s => s.OptimalRoutesCalculated = true);
        flow.Step("allocate-items", s => s.AllItemsAllocated = true);
        flow.Step("schedule-shipments", s => s.ShipmentsScheduled = s.RequestedItems.Count);
        flow.Step("optimize-cost", s => s.CostOptimized = true);
    }
}

public class HROnboardingFlow : FlowConfig<HROnboardingState>
{
    protected override void Configure(IFlowBuilder<HROnboardingState> flow)
    {
        flow.Name("hr-onboarding");

        flow.Send(s => new PerformBackgroundCheckCommand { EmployeeId = s.EmployeeId })
            .Into((s, r) => s.BackgroundCheckCompleted = r.Value);

        flow.Send(s => new RequestManagerApprovalCommand { EmployeeId = s.EmployeeId, ManagerId = s.ManagerId })
            .Into((s, r) => s.ManagerApproved = r.Value);

        flow.Step("hr-approval", s => s.HRApproved = true);

        flow.Send(s => new OrderEquipmentCommand { EmployeeId = s.EmployeeId })
            .Into((s, r) => s.EquipmentOrdered = r.Value);

        flow.Send(s => new CreateAccountsCommand { EmployeeId = s.EmployeeId })
            .Into((s, r) => s.AccountsCreated = r.Value);

        flow.Step("complete-onboarding", s => s.OnboardingCompleted = true);
    }
}

public class DocumentApprovalFlow : FlowConfig<DocumentApprovalState>
{
    protected override void Configure(IFlowBuilder<DocumentApprovalState> flow)
    {
        flow.Name("document-approval");

        flow.ForEach(s => s.Signatories)
            .Configure((signatory, f) =>
            {
                f.Send(s => new RequestSignatureCommand { DocumentId = s.DocumentId, Signatory = signatory })
                    .Into((s, result) =>
                    {
                        if (result.Value)
                            s.SignaturesCollected++;
                    });
            })
            .OnComplete(s => s.AllSignaturesReceived = s.SignaturesCollected == s.Signatories.Count)
            .EndForEach();

        flow.Send(s => new FinalizeDocumentCommand { DocumentId = s.DocumentId })
            .Into((s, r) => s.DocumentFinalized = r.Value);

        flow.Step("archive-document", s => s.ArchiveStored = true);
    }
}

public class ProjectManagementFlow : FlowConfig<ProjectManagementState>
{
    protected override void Configure(IFlowBuilder<ProjectManagementState> flow)
    {
        flow.Name("project-management");

        flow.Send(s => new CreateScheduleCommand { Tasks = s.Tasks })
            .Into((s, r) => s.ScheduleCreated = r.Value);

        flow.Send(s => new IdentifyCriticalPathCommand { Tasks = s.Tasks })
            .Into((s, r) =>
            {
                s.CriticalPathIdentified = true;
                s.TotalDuration = r.Value;
            });

        flow.Send(s => new AllocateResourcesCommand { ProjectId = s.ProjectId, Budget = s.Budget })
            .Into((s, r) => s.ResourcesAllocated = r.Value);

        flow.Step("start-project", s => s.ProjectStarted = true);
    }
}

// ========== States ==========

public class LoanApplicationState : IFlowState
{
    public string? FlowId { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public string ApplicantId { get; set; } = string.Empty;
    public decimal LoanAmount { get; set; }
    public int LoanTerm { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public int CreditScore { get; set; }
    public decimal AnnualIncome { get; set; }
    public string EmploymentStatus { get; set; } = string.Empty;
    public decimal ExistingDebts { get; set; }
    public bool CreditCheckCompleted { get; set; }
    public bool CreditApproved { get; set; }
    public bool IncomeVerified { get; set; }
    public bool DebtToIncomeRatioAcceptable { get; set; }
    public int ApprovalLevel { get; set; }
    public bool LoanApproved { get; set; }
    public decimal InterestRate { get; set; }
    public decimal MonthlyPayment { get; set; }
    public bool OfferGenerated { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class InsuranceClaimState : IFlowState
{
    public string? FlowId { get; set; }
    public string ClaimId { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string ClaimType { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public DateTime DateOfLoss { get; set; }
    public string IncidentDescription { get; set; } = string.Empty;
    public List<string> Documents { get; set; } = new();
    public bool ClaimValidated { get; set; }
    public bool DocumentsVerified { get; set; }
    public bool AssessmentCompleted { get; set; }
    public decimal AssessedAmount { get; set; }
    public bool ClaimApproved { get; set; }
    public bool PaymentProcessed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SupplyChainState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public List<SupplyItem> RequestedItems { get; set; } = new();
    public List<string> Warehouses { get; set; } = new();
    public DateTime DeliveryDeadline { get; set; }
    public bool InventoryChecked { get; set; }
    public bool OptimalRoutesCalculated { get; set; }
    public int ShipmentsScheduled { get; set; }
    public bool CostOptimized { get; set; }
    public bool AllItemsAllocated { get; set; }
    public decimal TotalCost { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class HROnboardingState : IFlowState
{
    public string? FlowId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string ManagerId { get; set; } = string.Empty;
    public decimal Salary { get; set; }
    public bool BackgroundCheckCompleted { get; set; }
    public bool ManagerApproved { get; set; }
    public bool HRApproved { get; set; }
    public bool EquipmentOrdered { get; set; }
    public bool AccountsCreated { get; set; }
    public bool OnboardingCompleted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class DocumentApprovalState : IFlowState
{
    public string? FlowId { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<Signatory> Signatories { get; set; } = new();
    public DateTime CreatedDate { get; set; }
    public DateTime DueDate { get; set; }
    public int SignaturesCollected { get; set; }
    public bool AllSignaturesReceived { get; set; }
    public bool DocumentFinalized { get; set; }
    public bool ArchiveStored { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ProjectManagementState : IFlowState
{
    public string? FlowId { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public List<ProjectTask> Tasks { get; set; } = new();
    public DateTime StartDate { get; set; }
    public decimal Budget { get; set; }
    public bool ScheduleCreated { get; set; }
    public bool CriticalPathIdentified { get; set; }
    public int TotalDuration { get; set; }
    public bool ResourcesAllocated { get; set; }
    public bool ProjectStarted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Supporting Models ==========

public class SupplyItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Priority { get; set; } = string.Empty;
}

public class Signatory
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ProjectTask
{
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Duration { get; set; }
    public List<string> Dependencies { get; set; } = new();
}

public class LoanApprovalResult
{
    public bool Approved { get; set; }
    public int ApprovalLevel { get; set; }
    public decimal InterestRate { get; set; }
    public decimal MonthlyPayment { get; set; }
}

public class InventoryStatus
{
    public string ProductId { get; set; } = string.Empty;
    public int Available { get; set; }
    public string Location { get; set; } = string.Empty;
}

// ========== Commands ==========

public class PerformCreditCheckCommand : IRequest<bool> { public int CreditScore { get; set; } }
public class VerifyIncomeCommand : IRequest<bool> { public decimal AnnualIncome { get; set; } public string EmploymentStatus { get; set; } = string.Empty; }
public class CalculateDebtToIncomeCommand : IRequest<decimal> { public decimal Income { get; set; } public decimal Debt { get; set; } }
public class DetermineLoanApprovalCommand : IRequest<LoanApprovalResult> { public decimal Amount { get; set; } public int CreditScore { get; set; } }
public class ValidateClaimCommand : IRequest<bool> { public string ClaimId { get; set; } = string.Empty; public string PolicyId { get; set; } = string.Empty; }
public class VerifyDocumentsCommand : IRequest<bool> { public List<string> Documents { get; set; } = new(); }
public class AssessClaimCommand : IRequest<decimal> { public decimal ClaimAmount { get; set; } public string ClaimType { get; set; } = string.Empty; }
public class ApproveClaimCommand : IRequest<bool> { public string ClaimId { get; set; } = string.Empty; public decimal AssessedAmount { get; set; } }
public class CheckInventoryCommand : IRequest<List<InventoryStatus>> { public List<SupplyItem> Items { get; set; } = new(); }
public class OptimizeRoutesCommand : IRequest<decimal> { public List<string> Warehouses { get; set; } = new(); public List<SupplyItem> Items { get; set; } = new(); }
public class PerformBackgroundCheckCommand : IRequest<bool> { public string EmployeeId { get; set; } = string.Empty; }
public class RequestManagerApprovalCommand : IRequest<bool> { public string EmployeeId { get; set; } = string.Empty; public string ManagerId { get; set; } = string.Empty; }
public class OrderEquipmentCommand : IRequest<bool> { public string EmployeeId { get; set; } = string.Empty; }
public class CreateAccountsCommand : IRequest<bool> { public string EmployeeId { get; set; } = string.Empty; }
public class RequestSignatureCommand : IRequest<bool> { public string DocumentId { get; set; } = string.Empty; public Signatory Signatory { get; set; } = new(); }
public class FinalizeDocumentCommand : IRequest<bool> { public string DocumentId { get; set; } = string.Empty; }
public class CreateScheduleCommand : IRequest<bool> { public List<ProjectTask> Tasks { get; set; } = new(); }
public class IdentifyCriticalPathCommand : IRequest<int> { public List<ProjectTask> Tasks { get; set; } = new(); }
public class AllocateResourcesCommand : IRequest<bool> { public string ProjectId { get; set; } = string.Empty; public decimal Budget { get; set; } }
