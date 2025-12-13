using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Real-world workflow scenario tests.
/// Tests practical business workflows like user registration, loan approval, and ticket booking.
/// </summary>
public class RealWorldFlowTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    #region User Registration Workflow

    [Fact]
    public async Task UserRegistration_Complete_RegistersSuccessfully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<UserRegistrationState>("user-registration")
            .Step("validate-email", async (state, ct) =>
            {
                state.Steps.Add("validate-email");
                state.EmailValid = state.Email.Contains("@");
                return state.EmailValid;
            })
            .Step("check-duplicate", async (state, ct) =>
            {
                state.Steps.Add("check-duplicate");
                state.IsDuplicate = state.ExistingEmails.Contains(state.Email);
                return !state.IsDuplicate;
            })
            .Step("hash-password", async (state, ct) =>
            {
                state.Steps.Add("hash-password");
                state.PasswordHash = $"hashed_{state.Password}";
                return true;
            })
            .Step("create-user", async (state, ct) =>
            {
                state.Steps.Add("create-user");
                state.UserId = Guid.NewGuid().ToString("N")[..12];
                return true;
            })
            .Step("send-verification", async (state, ct) =>
            {
                state.Steps.Add("send-verification");
                state.VerificationSent = true;
                return true;
            })
            .Build();

        var state = new UserRegistrationState
        {
            FlowId = "reg-test",
            Email = "user@example.com",
            Password = "secure123",
            ExistingEmails = new List<string> { "taken@example.com" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.UserId.Should().NotBeNullOrEmpty();
        result.State.VerificationSent.Should().BeTrue();
    }

    [Fact]
    public async Task UserRegistration_DuplicateEmail_Fails()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<UserRegistrationState>("user-registration")
            .Step("validate-email", async (state, ct) =>
            {
                state.EmailValid = state.Email.Contains("@");
                return state.EmailValid;
            })
            .Step("check-duplicate", async (state, ct) =>
            {
                state.IsDuplicate = state.ExistingEmails.Contains(state.Email);
                if (state.IsDuplicate) state.ErrorMessage = "Email already registered";
                return !state.IsDuplicate;
            })
            .Build();

        var state = new UserRegistrationState
        {
            FlowId = "dup-test",
            Email = "taken@example.com",
            ExistingEmails = new List<string> { "taken@example.com" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.IsDuplicate.Should().BeTrue();
    }

    #endregion

    #region Loan Approval Workflow

    [Fact]
    public async Task LoanApproval_HighCreditScore_AutoApproved()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoanApplicationState>("loan-approval")
            .Step("verify-identity", async (state, ct) =>
            {
                state.Steps.Add("verify-identity");
                state.IdentityVerified = true;
                return true;
            })
            .Step("check-credit", async (state, ct) =>
            {
                state.Steps.Add("check-credit");
                return true;
            })
            .If(s => s.CreditScore >= 750)
                .Then(f => f.Step("auto-approve", async (state, ct) =>
                {
                    state.Steps.Add("auto-approve");
                    state.Status = "Approved";
                    state.ApprovalType = "Automatic";
                    return true;
                }))
            .ElseIf(s => s.CreditScore >= 600)
                .Then(f => f.Step("manual-review", async (state, ct) =>
                {
                    state.Steps.Add("manual-review");
                    state.Status = "PendingReview";
                    state.ApprovalType = "Manual";
                    return true;
                }))
            .Else(f => f.Step("reject", async (state, ct) =>
            {
                state.Steps.Add("reject");
                state.Status = "Rejected";
                state.RejectionReason = "Credit score too low";
                return true;
            }))
            .EndIf()
            .Step("notify-applicant", async (state, ct) =>
            {
                state.Steps.Add("notify-applicant");
                state.NotificationSent = true;
                return true;
            })
            .Build();

        var state = new LoanApplicationState
        {
            FlowId = "loan-high",
            ApplicantName = "John Doe",
            CreditScore = 800,
            RequestedAmount = 50000m
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Approved");
        result.State.ApprovalType.Should().Be("Automatic");
    }

    [Fact]
    public async Task LoanApproval_LowCreditScore_Rejected()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoanApplicationState>("loan-approval")
            .Step("verify-identity", async (state, ct) =>
            {
                state.IdentityVerified = true;
                return true;
            })
            .If(s => s.CreditScore >= 750)
                .Then(f => f.Step("auto-approve", async (state, ct) =>
                {
                    state.Status = "Approved";
                    return true;
                }))
            .ElseIf(s => s.CreditScore >= 600)
                .Then(f => f.Step("manual-review", async (state, ct) =>
                {
                    state.Status = "PendingReview";
                    return true;
                }))
            .Else(f => f.Step("reject", async (state, ct) =>
            {
                state.Status = "Rejected";
                state.RejectionReason = "Credit score below minimum";
                return true;
            }))
            .EndIf()
            .Build();

        var state = new LoanApplicationState
        {
            FlowId = "loan-low",
            CreditScore = 450
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Rejected");
    }

    #endregion

    #region Ticket Booking Workflow

    [Fact]
    public async Task TicketBooking_AvailableSeats_BookSuccessfully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TicketBookingState>("ticket-booking")
            .Step("check-availability", async (state, ct) =>
            {
                state.Steps.Add("check-availability");
                state.SeatsAvailable = state.AvailableSeats >= state.RequestedSeats;
                return state.SeatsAvailable;
            })
            .Step("reserve-seats", async (state, ct) =>
            {
                state.Steps.Add("reserve-seats");
                state.ReservedSeats = state.RequestedSeats;
                state.AvailableSeats -= state.RequestedSeats;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.Steps.Add("release-seats");
                state.AvailableSeats += state.ReservedSeats;
                state.ReservedSeats = 0;
            })
            .Step("process-payment", async (state, ct) =>
            {
                state.Steps.Add("process-payment");
                state.TotalAmount = state.RequestedSeats * state.PricePerSeat;
                state.PaymentProcessed = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.Steps.Add("refund-payment");
                state.PaymentProcessed = false;
            })
            .Step("generate-tickets", async (state, ct) =>
            {
                state.Steps.Add("generate-tickets");
                state.BookingReference = $"BK-{Guid.NewGuid():N}"[..10];
                for (int i = 0; i < state.RequestedSeats; i++)
                {
                    state.TicketNumbers.Add($"TKT-{i + 1:000}");
                }
                return true;
            })
            .Step("send-confirmation", async (state, ct) =>
            {
                state.Steps.Add("send-confirmation");
                state.ConfirmationSent = true;
                return true;
            })
            .Build();

        var state = new TicketBookingState
        {
            FlowId = "book-test",
            EventName = "Concert 2024",
            RequestedSeats = 3,
            AvailableSeats = 100,
            PricePerSeat = 75m
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.TicketNumbers.Should().HaveCount(3);
        result.State.TotalAmount.Should().Be(225m);
        result.State.ConfirmationSent.Should().BeTrue();
    }

    [Fact]
    public async Task TicketBooking_PaymentFails_RollsBack()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TicketBookingState>("ticket-booking-fail")
            .Step("check-availability", async (state, ct) =>
            {
                state.SeatsAvailable = state.AvailableSeats >= state.RequestedSeats;
                return state.SeatsAvailable;
            })
            .Step("reserve-seats", async (state, ct) =>
            {
                state.ReservedSeats = state.RequestedSeats;
                state.AvailableSeats -= state.RequestedSeats;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.AvailableSeats += state.ReservedSeats;
                state.ReservedSeats = 0;
                state.Steps.Add("seats-released");
            })
            .Step("process-payment", async (state, ct) =>
            {
                if (state.SimulatePaymentFailure)
                {
                    throw new InvalidOperationException("Payment declined");
                }
                return true;
            })
            .Build();

        var state = new TicketBookingState
        {
            FlowId = "book-fail",
            RequestedSeats = 2,
            AvailableSeats = 50,
            SimulatePaymentFailure = true
        };
        var originalSeats = state.AvailableSeats;

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.AvailableSeats.Should().Be(originalSeats); // Seats restored
        result.State.Steps.Should().Contain("seats-released");
    }

    #endregion

    #region Insurance Claim Workflow

    [Fact]
    public async Task InsuranceClaim_UnderThreshold_AutoApproved()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<InsuranceClaimState>("insurance-claim")
            .Step("validate-policy", async (state, ct) =>
            {
                state.Steps.Add("validate-policy");
                state.PolicyValid = state.PolicyExpiryDate > DateTime.UtcNow;
                return state.PolicyValid;
            })
            .Step("assess-damage", async (state, ct) =>
            {
                state.Steps.Add("assess-damage");
                state.AssessedAmount = state.ClaimAmount * 0.9m; // 90% coverage
                return true;
            })
            .If(s => s.ClaimAmount <= s.AutoApprovalThreshold)
                .Then(f => f.Step("auto-approve", async (state, ct) =>
                {
                    state.Steps.Add("auto-approve");
                    state.Status = "Approved";
                    state.ApprovedAmount = state.AssessedAmount;
                    return true;
                }))
            .Else(f => f.Step("manual-review", async (state, ct) =>
            {
                state.Steps.Add("manual-review");
                state.Status = "PendingReview";
                state.RequiresInvestigation = true;
                return true;
            }))
            .EndIf()
            .Step("process-payout", async (state, ct) =>
            {
                if (state.Status == "Approved")
                {
                    state.Steps.Add("process-payout");
                    state.PayoutProcessed = true;
                }
                return true;
            })
            .Build();

        var state = new InsuranceClaimState
        {
            FlowId = "claim-auto",
            PolicyNumber = "POL-001",
            PolicyExpiryDate = DateTime.UtcNow.AddYears(1),
            ClaimAmount = 500m,
            AutoApprovalThreshold = 1000m
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Approved");
        result.State.PayoutProcessed.Should().BeTrue();
    }

    #endregion

    #region Document Approval Workflow

    [Fact]
    public async Task DocumentApproval_MultiLevel_CompletesAllLevels()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DocumentApprovalState>("document-approval")
            .Step("submit", async (state, ct) =>
            {
                state.Steps.Add("submit");
                state.Status = "Submitted";
                return true;
            })
            .ForEach(
                s => s.ApprovalLevels,
                (level, f) => f.Step($"approve-level-{level}", async (state, ct) =>
                {
                    state.Steps.Add($"approve-{level}");
                    state.Approvals.Add(new Approval(level, $"approver-{level}", DateTime.UtcNow));
                    return true;
                }))
            .Step("finalize", async (state, ct) =>
            {
                state.Steps.Add("finalize");
                state.Status = "Approved";
                state.FinalizedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var state = new DocumentApprovalState
        {
            FlowId = "doc-approval",
            DocumentId = "DOC-001",
            ApprovalLevels = new List<string> { "Manager", "Director", "VP" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Approved");
        result.State.Approvals.Should().HaveCount(3);
    }

    #endregion

    #region State Classes

    public class UserRegistrationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public List<string> ExistingEmails { get; set; } = new();
        public bool EmailValid { get; set; }
        public bool IsDuplicate { get; set; }
        public string? PasswordHash { get; set; }
        public string? UserId { get; set; }
        public bool VerificationSent { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class LoanApplicationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string ApplicantName { get; set; } = "";
        public int CreditScore { get; set; }
        public decimal RequestedAmount { get; set; }
        public bool IdentityVerified { get; set; }
        public string Status { get; set; } = "";
        public string? ApprovalType { get; set; }
        public string? RejectionReason { get; set; }
        public bool NotificationSent { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class TicketBookingState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string EventName { get; set; } = "";
        public int RequestedSeats { get; set; }
        public int AvailableSeats { get; set; }
        public int ReservedSeats { get; set; }
        public decimal PricePerSeat { get; set; }
        public decimal TotalAmount { get; set; }
        public bool SeatsAvailable { get; set; }
        public bool PaymentProcessed { get; set; }
        public string? BookingReference { get; set; }
        public List<string> TicketNumbers { get; set; } = new();
        public bool ConfirmationSent { get; set; }
        public bool SimulatePaymentFailure { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class InsuranceClaimState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string PolicyNumber { get; set; } = "";
        public DateTime PolicyExpiryDate { get; set; }
        public decimal ClaimAmount { get; set; }
        public decimal AutoApprovalThreshold { get; set; }
        public bool PolicyValid { get; set; }
        public decimal AssessedAmount { get; set; }
        public decimal ApprovedAmount { get; set; }
        public string Status { get; set; } = "";
        public bool RequiresInvestigation { get; set; }
        public bool PayoutProcessed { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class DocumentApprovalState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string DocumentId { get; set; } = "";
        public List<string> ApprovalLevels { get; set; } = new();
        public List<Approval> Approvals { get; set; } = new();
        public string Status { get; set; } = "";
        public DateTime? FinalizedAt { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public record Approval(string Level, string ApproverName, DateTime ApprovedAt);

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
