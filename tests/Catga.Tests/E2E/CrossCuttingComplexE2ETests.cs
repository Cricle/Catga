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
/// Cross-cutting complex E2E tests with multiple intersecting conditions.
/// Tests sophisticated scenarios with 3+ levels of conditional logic and interdependencies.
/// </summary>
public class CrossCuttingComplexE2ETests
{
    private readonly ITestOutputHelper _output;

    public CrossCuttingComplexE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task E2E_CreditCardApplication_ComplexMultiLevelDecision()
    {
        // Arrange - Credit card with 4-level decision logic:
        // Level 1: Income check
        // Level 2: Credit score check
        // Level 3: Debt ratio check
        // Level 4: Employment stability check
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<CreditCardApplicationState, CreditCardApplicationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CreditCardApplicationState, CreditCardApplicationFlow>>();

        var application = new CreditCardApplicationState
        {
            FlowId = "cc-001",
            ApplicationId = "APP-CC-001",
            ApplicantId = "APPL-001",
            AnnualIncome = 80000.00m,
            CreditScore = 720,
            ExistingDebt = 15000.00m,
            EmploymentYears = 4,
            EmploymentType = "FullTime",
            RequestedLimit = 5000.00m,
            Age = 35,
            HasBankAccount = true,
            PreviousDefaults = 0
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(application);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IncomeQualifies.Should().BeTrue();
        result.State.CreditScoreQualifies.Should().BeTrue();
        result.State.DebtRatioAcceptable.Should().BeTrue();
        result.State.EmploymentStable.Should().BeTrue();
        result.State.ApplicationApproved.Should().BeTrue();
        result.State.ApprovedLimit.Should().BeGreaterThan(0);
        result.State.DecisionPath.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✓ Credit card application (4-level decision) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Income: ${result.State.AnnualIncome:F2}");
        _output.WriteLine($"  Credit Score: {result.State.CreditScore}");
        _output.WriteLine($"  Debt Ratio: {(result.State.ExistingDebt / result.State.AnnualIncome):P}");
        _output.WriteLine($"  Decision: {result.State.DecisionPath}");
        _output.WriteLine($"  Approved Limit: ${result.State.ApprovedLimit:F2}");
    }

    [Fact]
    public async Task E2E_InsurancePremiumCalculation_ComplexRatingFactors()
    {
        // Arrange - Insurance with 5 intersecting rating factors:
        // Age + Health + Driving Record + Coverage Type + Location
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<InsurancePremiumState, InsurancePremiumCalculationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<InsurancePremiumState, InsurancePremiumCalculationFlow>>();

        var insurance = new InsurancePremiumState
        {
            FlowId = "ins-001",
            PolicyId = "POL-INS-001",
            ApplicantId = "APPL-INS-001",
            Age = 28,
            HealthStatus = "Excellent",
            DrivingRecordScore = 95,
            CoverageType = "Comprehensive",
            Location = "Urban",
            BaseRate = 1000.00m,
            VehicleValue = 30000.00m,
            VehicleAge = 3
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(insurance);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AgeFactorCalculated.Should().BeTrue();
        result.State.HealthFactorCalculated.Should().BeTrue();
        result.State.DrivingRecordFactorCalculated.Should().BeTrue();
        result.State.CoverageFactorCalculated.Should().BeTrue();
        result.State.LocationFactorCalculated.Should().BeTrue();
        result.State.FinalPremium.Should().BeGreaterThan(result.State.BaseRate);
        result.State.RiskCategory.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✓ Insurance premium calculation (5 factors) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Base Rate: ${result.State.BaseRate:F2}");
        _output.WriteLine($"  Final Premium: ${result.State.FinalPremium:F2}");
        _output.WriteLine($"  Risk Category: {result.State.RiskCategory}");
        _output.WriteLine($"  Total Multiplier: {(result.State.FinalPremium / result.State.BaseRate):F2}x");
    }

    [Fact]
    public async Task E2E_EmployeePromotion_ComplexEligibilityMatrix()
    {
        // Arrange - Promotion with 6 intersecting eligibility criteria:
        // Performance + Tenure + Education + Skills + Availability + Budget
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<EmployeePromotionState, EmployeePromotionFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EmployeePromotionState, EmployeePromotionFlow>>();

        var promotion = new EmployeePromotionState
        {
            FlowId = "promo-001",
            EmployeeId = "EMP-PROMO-001",
            CurrentPosition = "Senior Developer",
            TargetPosition = "Tech Lead",
            PerformanceRating = 4.5m,
            YearsInRole = 3,
            EducationLevel = "Masters",
            RequiredSkills = new List<string> { "Leadership", "Architecture", "Mentoring" },
            HasRequiredSkills = true,
            AvailableImmediately = true,
            BudgetAvailable = 150000.00m,
            CurrentSalary = 120000.00m,
            PromotionSalary = 140000.00m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(promotion);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.PerformanceQualifies.Should().BeTrue();
        result.State.TenureQualifies.Should().BeTrue();
        result.State.EducationQualifies.Should().BeTrue();
        result.State.SkillsQualify.Should().BeTrue();
        result.State.AvailabilityConfirmed.Should().BeTrue();
        result.State.BudgetApproved.Should().BeTrue();
        result.State.PromotionApproved.Should().BeTrue();
        result.State.ApprovalPath.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✓ Employee promotion (6 criteria) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Current: {result.State.CurrentPosition} @ ${result.State.CurrentSalary:F2}");
        _output.WriteLine($"  Target: {result.State.TargetPosition} @ ${result.State.PromotionSalary:F2}");
        _output.WriteLine($"  Approval Path: {result.State.ApprovalPath}");
    }

    [Fact]
    public async Task E2E_MortgageApproval_ComplexUnderwriting()
    {
        // Arrange - Mortgage with 7 intersecting underwriting factors:
        // Debt-to-Income + Loan-to-Value + Credit Score + Employment + Assets + Property + Market
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MortgageApplicationState, MortgageUnderwritingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MortgageApplicationState, MortgageUnderwritingFlow>>();

        var mortgage = new MortgageApplicationState
        {
            FlowId = "mort-001",
            ApplicationId = "APP-MORT-001",
            ApplicantId = "APPL-MORT-001",
            LoanAmount = 400000.00m,
            PropertyValue = 500000.00m,
            AnnualIncome = 150000.00m,
            ExistingDebts = 30000.00m,
            CreditScore = 760,
            EmploymentYears = 8,
            LiquidAssets = 100000.00m,
            DownPaymentPercent = 0.20m,
            PropertyType = "SingleFamily",
            PropertyLocation = "DesirableArea",
            MarketCondition = "Stable"
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(mortgage);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.DebtToIncomeAcceptable.Should().BeTrue();
        result.State.LoanToValueAcceptable.Should().BeTrue();
        result.State.CreditQualifies.Should().BeTrue();
        result.State.EmploymentVerified.Should().BeTrue();
        result.State.AssetsVerified.Should().BeTrue();
        result.State.PropertyAppraisalPassed.Should().BeTrue();
        result.State.MarketAnalysisPassed.Should().BeTrue();
        result.State.MortgageApproved.Should().BeTrue();
        result.State.InterestRate.Should().BeGreaterThan(0);

        _output.WriteLine($"✓ Mortgage underwriting (7 factors) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Loan Amount: ${result.State.LoanAmount:F2}");
        _output.WriteLine($"  LTV: {(result.State.LoanAmount / result.State.PropertyValue):P}");
        _output.WriteLine($"  DTI: {((result.State.ExistingDebts + result.State.LoanAmount * 0.005m) / result.State.AnnualIncome):P}");
        _output.WriteLine($"  Interest Rate: {result.State.InterestRate:P2}");
    }

    [Fact]
    public async Task E2E_SupplierOnboarding_ComplexComplianceMatrix()
    {
        // Arrange - Supplier onboarding with 8 intersecting compliance checks:
        // Financial + Legal + Tax + Insurance + Certifications + References + Audit + Contract
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SupplierOnboardingState, SupplierOnboardingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SupplierOnboardingState, SupplierOnboardingFlow>>();

        var supplier = new SupplierOnboardingState
        {
            FlowId = "supp-001",
            SupplierId = "SUPP-001",
            CompanyName = "TechSupply Corp",
            FinancialStability = "Strong",
            LegalStatus = "Registered",
            TaxCompliance = "Current",
            InsuranceCoverage = "Adequate",
            Certifications = new List<string> { "ISO9001", "ISO27001" },
            References = new List<string> { "Ref1", "Ref2", "Ref3" },
            AuditScore = 95,
            ContractTermsAccepted = true,
            MinimumOrderValue = 1000.00m,
            PaymentTerms = "Net30"
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(supplier);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.FinancialCheckPassed.Should().BeTrue();
        result.State.LegalCheckPassed.Should().BeTrue();
        result.State.TaxCheckPassed.Should().BeTrue();
        result.State.InsuranceCheckPassed.Should().BeTrue();
        result.State.CertificationsVerified.Should().BeTrue();
        result.State.ReferencesVerified.Should().BeTrue();
        result.State.AuditPassed.Should().BeTrue();
        result.State.ContractSigned.Should().BeTrue();
        result.State.OnboardingComplete.Should().BeTrue();
        result.State.ApprovalStatus.Should().Be("FullyApproved");

        _output.WriteLine($"✓ Supplier onboarding (8 checks) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Company: {result.State.CompanyName}");
        _output.WriteLine($"  Audit Score: {result.State.AuditScore}");
        _output.WriteLine($"  Approval Status: {result.State.ApprovalStatus}");
    }

    [Fact]
    public async Task E2E_ProjectRiskAssessment_ComplexMultiDimensional()
    {
        // Arrange - Project risk with 9 intersecting dimensions:
        // Schedule + Budget + Resources + Technology + Stakeholders + Dependencies + Scope + Quality + Market
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ProjectRiskAssessmentState, ProjectRiskAssessmentFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ProjectRiskAssessmentState, ProjectRiskAssessmentFlow>>();

        var assessment = new ProjectRiskAssessmentState
        {
            FlowId = "risk-001",
            ProjectId = "PROJ-RISK-001",
            ProjectName = "Enterprise Migration",
            ScheduleRisk = 0.3m,
            BudgetRisk = 0.2m,
            ResourceRisk = 0.4m,
            TechnologyRisk = 0.5m,
            StakeholderRisk = 0.2m,
            DependencyRisk = 0.6m,
            ScopeRisk = 0.3m,
            QualityRisk = 0.25m,
            MarketRisk = 0.15m,
            TotalBudget = 500000.00m,
            TimelineMonths = 12,
            TeamSize = 15
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(assessment);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ScheduleRiskAssessed.Should().BeTrue();
        result.State.BudgetRiskAssessed.Should().BeTrue();
        result.State.ResourceRiskAssessed.Should().BeTrue();
        result.State.TechnologyRiskAssessed.Should().BeTrue();
        result.State.StakeholderRiskAssessed.Should().BeTrue();
        result.State.DependencyRiskAssessed.Should().BeTrue();
        result.State.ScopeRiskAssessed.Should().BeTrue();
        result.State.QualityRiskAssessed.Should().BeTrue();
        result.State.MarketRiskAssessed.Should().BeTrue();
        result.State.OverallRiskLevel.Should().NotBeNullOrEmpty();
        result.State.MitigationPlanRequired.Should().BeTrue();

        _output.WriteLine($"✓ Project risk assessment (9 dimensions) in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Project: {result.State.ProjectName}");
        _output.WriteLine($"  Overall Risk: {result.State.OverallRiskLevel}");
        _output.WriteLine($"  Highest Risk: {result.State.HighestRiskDimension}");
        _output.WriteLine($"  Mitigation Required: {result.State.MitigationPlanRequired}");
    }
}

// ========== Flow Configurations ==========

public class CreditCardApplicationFlow : FlowConfig<CreditCardApplicationState>
{
    protected override void Configure(IFlowBuilder<CreditCardApplicationState> flow)
    {
        flow.Name("credit-card-application");

        flow.Step("check-income", s => s.IncomeQualifies = s.AnnualIncome >= 30000);
        flow.Step("check-credit-score", s => s.CreditScoreQualifies = s.CreditScore >= 650);
        flow.Step("check-debt-ratio", s =>
        {
            var debtRatio = s.ExistingDebt / s.AnnualIncome;
            s.DebtRatioAcceptable = debtRatio <= 0.5m;
        });
        flow.Step("check-employment", s => s.EmploymentStable = s.EmploymentYears >= 2);

        flow.If(s => s.IncomeQualifies && s.CreditScoreQualifies && s.DebtRatioAcceptable && s.EmploymentStable)
            .Then(f => f.Step("approve-premium", s =>
            {
                s.ApplicationApproved = true;
                s.ApprovedLimit = s.RequestedLimit * 1.5m;
                s.DecisionPath = "PremiumApproval";
            }))
            .ElseIf(s => s.IncomeQualifies && s.CreditScoreQualifies && s.DebtRatioAcceptable)
            .Then(f => f.Step("approve-standard", s =>
            {
                s.ApplicationApproved = true;
                s.ApprovedLimit = s.RequestedLimit;
                s.DecisionPath = "StandardApproval";
            }))
            .ElseIf(s => s.IncomeQualifies && s.CreditScoreQualifies)
            .Then(f => f.Step("approve-limited", s =>
            {
                s.ApplicationApproved = true;
                s.ApprovedLimit = s.RequestedLimit * 0.5m;
                s.DecisionPath = "LimitedApproval";
            }))
            .Else(f => f.Step("reject", s =>
            {
                s.ApplicationApproved = false;
                s.ApprovedLimit = 0;
                s.DecisionPath = "Rejected";
            }))
            .EndIf();
    }
}

public class InsurancePremiumCalculationFlow : FlowConfig<InsurancePremiumState>
{
    protected override void Configure(IFlowBuilder<InsurancePremiumState> flow)
    {
        flow.Name("insurance-premium-calculation");

        flow.Step("calculate-age-factor", s =>
        {
            s.AgeFactorCalculated = true;
            s.AgeFactor = s.Age < 25 ? 1.5m : (s.Age > 65 ? 1.3m : 1.0m);
        });

        flow.Step("calculate-health-factor", s =>
        {
            s.HealthFactorCalculated = true;
            s.HealthFactor = s.HealthStatus == "Excellent" ? 0.8m : (s.HealthStatus == "Good" ? 1.0m : 1.2m);
        });

        flow.Step("calculate-driving-factor", s =>
        {
            s.DrivingRecordFactorCalculated = true;
            s.DrivingFactor = s.DrivingRecordScore >= 90 ? 0.9m : (s.DrivingRecordScore >= 75 ? 1.0m : 1.3m);
        });

        flow.Step("calculate-coverage-factor", s =>
        {
            s.CoverageFactorCalculated = true;
            s.CoverageFactor = s.CoverageType == "Comprehensive" ? 1.2m : (s.CoverageType == "Collision" ? 1.1m : 1.0m);
        });

        flow.Step("calculate-location-factor", s =>
        {
            s.LocationFactorCalculated = true;
            s.LocationFactor = s.Location == "Urban" ? 1.3m : (s.Location == "Suburban" ? 1.1m : 0.9m);
        });

        flow.Step("calculate-final-premium", s =>
        {
            var multiplier = s.AgeFactor * s.HealthFactor * s.DrivingFactor * s.CoverageFactor * s.LocationFactor;
            s.FinalPremium = s.BaseRate * multiplier;
            s.RiskCategory = multiplier > 1.3m ? "High" : (multiplier > 1.0m ? "Medium" : "Low");
        });
    }
}

public class EmployeePromotionFlow : FlowConfig<EmployeePromotionState>
{
    protected override void Configure(IFlowBuilder<EmployeePromotionState> flow)
    {
        flow.Name("employee-promotion");

        flow.Step("check-performance", s => s.PerformanceQualifies = s.PerformanceRating >= 4.0m);
        flow.Step("check-tenure", s => s.TenureQualifies = s.YearsInRole >= 2);
        flow.Step("check-education", s => s.EducationQualifies = s.EducationLevel == "Masters" || s.EducationLevel == "PhD");
        flow.Step("check-skills", s => s.SkillsQualify = s.HasRequiredSkills);
        flow.Step("check-availability", s => s.AvailabilityConfirmed = s.AvailableImmediately);
        flow.Step("check-budget", s => s.BudgetApproved = s.PromotionSalary <= s.BudgetAvailable);

        flow.If(s => s.PerformanceQualifies && s.TenureQualifies && s.EducationQualifies && s.SkillsQualify && s.AvailabilityConfirmed && s.BudgetApproved)
            .Then(f => f.Step("approve-promotion", s =>
            {
                s.PromotionApproved = true;
                s.ApprovalPath = "FullApproval";
            }))
            .Else(f => f.Step("defer-promotion", s =>
            {
                s.PromotionApproved = false;
                s.ApprovalPath = "Deferred";
            }))
            .EndIf();
    }
}

public class MortgageUnderwritingFlow : FlowConfig<MortgageApplicationState>
{
    protected override void Configure(IFlowBuilder<MortgageApplicationState> flow)
    {
        flow.Name("mortgage-underwriting");

        flow.Step("calculate-dti", s =>
        {
            var monthlyIncome = s.AnnualIncome / 12;
            var monthlyPayment = s.LoanAmount * 0.005m;
            var totalMonthlyDebt = (s.ExistingDebts / 12) + monthlyPayment;
            s.DebtToIncomeRatio = totalMonthlyDebt / monthlyIncome;
            s.DebtToIncomeAcceptable = s.DebtToIncomeRatio <= 0.43m;
        });

        flow.Step("calculate-ltv", s =>
        {
            s.LoanToValueRatio = s.LoanAmount / s.PropertyValue;
            s.LoanToValueAcceptable = s.LoanToValueRatio <= 0.80m;
        });

        flow.Step("check-credit", s => s.CreditQualifies = s.CreditScore >= 740);
        flow.Step("verify-employment", s => s.EmploymentVerified = s.EmploymentYears >= 2);
        flow.Step("verify-assets", s => s.AssetsVerified = s.LiquidAssets >= s.LoanAmount * 0.05m);
        flow.Step("appraise-property", s => s.PropertyAppraisalPassed = s.PropertyType == "SingleFamily" || s.PropertyType == "Condo");
        flow.Step("analyze-market", s => s.MarketAnalysisPassed = s.MarketCondition == "Stable" || s.MarketCondition == "Appreciating");

        flow.If(s => s.DebtToIncomeAcceptable && s.LoanToValueAcceptable && s.CreditQualifies && s.EmploymentVerified && s.AssetsVerified && s.PropertyAppraisalPassed && s.MarketAnalysisPassed)
            .Then(f => f.Step("approve-mortgage", s =>
            {
                s.MortgageApproved = true;
                s.InterestRate = 0.065m;
            }))
            .Else(f => f.Step("deny-mortgage", s =>
            {
                s.MortgageApproved = false;
                s.InterestRate = 0;
            }))
            .EndIf();
    }
}

public class SupplierOnboardingFlow : FlowConfig<SupplierOnboardingState>
{
    protected override void Configure(IFlowBuilder<SupplierOnboardingState> flow)
    {
        flow.Name("supplier-onboarding");

        flow.Step("financial-check", s => s.FinancialCheckPassed = s.FinancialStability == "Strong" || s.FinancialStability == "Stable");
        flow.Step("legal-check", s => s.LegalCheckPassed = s.LegalStatus == "Registered");
        flow.Step("tax-check", s => s.TaxCheckPassed = s.TaxCompliance == "Current");
        flow.Step("insurance-check", s => s.InsuranceCheckPassed = s.InsuranceCoverage == "Adequate" || s.InsuranceCoverage == "Excellent");
        flow.Step("certifications-check", s => s.CertificationsVerified = s.Certifications.Count >= 2);
        flow.Step("references-check", s => s.ReferencesVerified = s.References.Count >= 3);
        flow.Step("audit-check", s => s.AuditPassed = s.AuditScore >= 80);
        flow.Step("contract-check", s => s.ContractSigned = s.ContractTermsAccepted);

        flow.If(s => s.FinancialCheckPassed && s.LegalCheckPassed && s.TaxCheckPassed && s.InsuranceCheckPassed && s.CertificationsVerified && s.ReferencesVerified && s.AuditPassed && s.ContractSigned)
            .Then(f => f.Step("complete-onboarding", s =>
            {
                s.OnboardingComplete = true;
                s.ApprovalStatus = "FullyApproved";
            }))
            .Else(f => f.Step("defer-onboarding", s =>
            {
                s.OnboardingComplete = false;
                s.ApprovalStatus = "Pending";
            }))
            .EndIf();
    }
}

public class ProjectRiskAssessmentFlow : FlowConfig<ProjectRiskAssessmentState>
{
    protected override void Configure(IFlowBuilder<ProjectRiskAssessmentState> flow)
    {
        flow.Name("project-risk-assessment");

        flow.Step("assess-schedule", s => s.ScheduleRiskAssessed = true);
        flow.Step("assess-budget", s => s.BudgetRiskAssessed = true);
        flow.Step("assess-resources", s => s.ResourceRiskAssessed = true);
        flow.Step("assess-technology", s => s.TechnologyRiskAssessed = true);
        flow.Step("assess-stakeholders", s => s.StakeholderRiskAssessed = true);
        flow.Step("assess-dependencies", s => s.DependencyRiskAssessed = true);
        flow.Step("assess-scope", s => s.ScopeRiskAssessed = true);
        flow.Step("assess-quality", s => s.QualityRiskAssessed = true);
        flow.Step("assess-market", s => s.MarketRiskAssessed = true);

        flow.Step("calculate-overall-risk", s =>
        {
            var avgRisk = (s.ScheduleRisk + s.BudgetRisk + s.ResourceRisk + s.TechnologyRisk + s.StakeholderRisk + s.DependencyRisk + s.ScopeRisk + s.QualityRisk + s.MarketRisk) / 9;
            s.OverallRiskLevel = avgRisk > 0.5m ? "High" : (avgRisk > 0.3m ? "Medium" : "Low");

            var risks = new[] { ("Schedule", s.ScheduleRisk), ("Budget", s.BudgetRisk), ("Resource", s.ResourceRisk), ("Technology", s.TechnologyRisk), ("Stakeholder", s.StakeholderRisk), ("Dependency", s.DependencyRisk), ("Scope", s.ScopeRisk), ("Quality", s.QualityRisk), ("Market", s.MarketRisk) };
            var highest = risks.OrderByDescending(r => r.Item2).First();
            s.HighestRiskDimension = highest.Item1;
            s.MitigationPlanRequired = avgRisk > 0.3m;
        });
    }
}

// ========== States ==========

public class CreditCardApplicationState : IFlowState
{
    public string? FlowId { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public string ApplicantId { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }
    public int CreditScore { get; set; }
    public decimal ExistingDebt { get; set; }
    public int EmploymentYears { get; set; }
    public string EmploymentType { get; set; } = string.Empty;
    public decimal RequestedLimit { get; set; }
    public int Age { get; set; }
    public bool HasBankAccount { get; set; }
    public int PreviousDefaults { get; set; }
    public bool IncomeQualifies { get; set; }
    public bool CreditScoreQualifies { get; set; }
    public bool DebtRatioAcceptable { get; set; }
    public bool EmploymentStable { get; set; }
    public bool ApplicationApproved { get; set; }
    public decimal ApprovedLimit { get; set; }
    public string DecisionPath { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class InsurancePremiumState : IFlowState
{
    public string? FlowId { get; set; }
    public string PolicyId { get; set; } = string.Empty;
    public string ApplicantId { get; set; } = string.Empty;
    public int Age { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public int DrivingRecordScore { get; set; }
    public string CoverageType { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal BaseRate { get; set; }
    public decimal VehicleValue { get; set; }
    public int VehicleAge { get; set; }
    public bool AgeFactorCalculated { get; set; }
    public decimal AgeFactor { get; set; }
    public bool HealthFactorCalculated { get; set; }
    public decimal HealthFactor { get; set; }
    public bool DrivingRecordFactorCalculated { get; set; }
    public decimal DrivingFactor { get; set; }
    public bool CoverageFactorCalculated { get; set; }
    public decimal CoverageFactor { get; set; }
    public bool LocationFactorCalculated { get; set; }
    public decimal LocationFactor { get; set; }
    public decimal FinalPremium { get; set; }
    public string RiskCategory { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class EmployeePromotionState : IFlowState
{
    public string? FlowId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string CurrentPosition { get; set; } = string.Empty;
    public string TargetPosition { get; set; } = string.Empty;
    public decimal PerformanceRating { get; set; }
    public int YearsInRole { get; set; }
    public string EducationLevel { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = new();
    public bool HasRequiredSkills { get; set; }
    public bool AvailableImmediately { get; set; }
    public decimal BudgetAvailable { get; set; }
    public decimal CurrentSalary { get; set; }
    public decimal PromotionSalary { get; set; }
    public bool PerformanceQualifies { get; set; }
    public bool TenureQualifies { get; set; }
    public bool EducationQualifies { get; set; }
    public bool SkillsQualify { get; set; }
    public bool AvailabilityConfirmed { get; set; }
    public bool BudgetApproved { get; set; }
    public bool PromotionApproved { get; set; }
    public string ApprovalPath { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class MortgageApplicationState : IFlowState
{
    public string? FlowId { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public string ApplicantId { get; set; } = string.Empty;
    public decimal LoanAmount { get; set; }
    public decimal PropertyValue { get; set; }
    public decimal AnnualIncome { get; set; }
    public decimal ExistingDebts { get; set; }
    public int CreditScore { get; set; }
    public int EmploymentYears { get; set; }
    public decimal LiquidAssets { get; set; }
    public decimal DownPaymentPercent { get; set; }
    public string PropertyType { get; set; } = string.Empty;
    public string PropertyLocation { get; set; } = string.Empty;
    public string MarketCondition { get; set; } = string.Empty;
    public decimal DebtToIncomeRatio { get; set; }
    public bool DebtToIncomeAcceptable { get; set; }
    public decimal LoanToValueRatio { get; set; }
    public bool LoanToValueAcceptable { get; set; }
    public bool CreditQualifies { get; set; }
    public bool EmploymentVerified { get; set; }
    public bool AssetsVerified { get; set; }
    public bool PropertyAppraisalPassed { get; set; }
    public bool MarketAnalysisPassed { get; set; }
    public bool MortgageApproved { get; set; }
    public decimal InterestRate { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SupplierOnboardingState : IFlowState
{
    public string? FlowId { get; set; }
    public string SupplierId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string FinancialStability { get; set; } = string.Empty;
    public string LegalStatus { get; set; } = string.Empty;
    public string TaxCompliance { get; set; } = string.Empty;
    public string InsuranceCoverage { get; set; } = string.Empty;
    public List<string> Certifications { get; set; } = new();
    public List<string> References { get; set; } = new();
    public int AuditScore { get; set; }
    public bool ContractTermsAccepted { get; set; }
    public decimal MinimumOrderValue { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
    public bool FinancialCheckPassed { get; set; }
    public bool LegalCheckPassed { get; set; }
    public bool TaxCheckPassed { get; set; }
    public bool InsuranceCheckPassed { get; set; }
    public bool CertificationsVerified { get; set; }
    public bool ReferencesVerified { get; set; }
    public bool AuditPassed { get; set; }
    public bool ContractSigned { get; set; }
    public bool OnboardingComplete { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ProjectRiskAssessmentState : IFlowState
{
    public string? FlowId { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public decimal ScheduleRisk { get; set; }
    public decimal BudgetRisk { get; set; }
    public decimal ResourceRisk { get; set; }
    public decimal TechnologyRisk { get; set; }
    public decimal StakeholderRisk { get; set; }
    public decimal DependencyRisk { get; set; }
    public decimal ScopeRisk { get; set; }
    public decimal QualityRisk { get; set; }
    public decimal MarketRisk { get; set; }
    public decimal TotalBudget { get; set; }
    public int TimelineMonths { get; set; }
    public int TeamSize { get; set; }
    public bool ScheduleRiskAssessed { get; set; }
    public bool BudgetRiskAssessed { get; set; }
    public bool ResourceRiskAssessed { get; set; }
    public bool TechnologyRiskAssessed { get; set; }
    public bool StakeholderRiskAssessed { get; set; }
    public bool DependencyRiskAssessed { get; set; }
    public bool ScopeRiskAssessed { get; set; }
    public bool QualityRiskAssessed { get; set; }
    public bool MarketRiskAssessed { get; set; }
    public string OverallRiskLevel { get; set; } = string.Empty;
    public string HighestRiskDimension { get; set; } = string.Empty;
    public bool MitigationPlanRequired { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
