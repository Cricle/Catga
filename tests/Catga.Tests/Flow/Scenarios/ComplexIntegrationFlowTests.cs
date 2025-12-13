using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Complex integration flow scenario tests.
/// Tests combined patterns including concurrency, error handling, fallback, circuit breaker, and more.
/// </summary>
public class ComplexIntegrationFlowTests
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

    [Fact]
    public async Task Complex_OrderWithPaymentAndInventory_HandlesAllScenarios()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var inventoryService = new MockInventoryService();
        var paymentService = new MockPaymentService();
        var notificationService = new MockNotificationService();

        var flow = FlowBuilder.Create<ComplexOrderState>("complex-order")
            // Step 1: Validate order
            .Step("validate", async (state, ct) =>
            {
                state.Log.Add("Validating order");
                state.IsValid = state.Items.Any() && state.CustomerId != null;
                return state.IsValid;
            })
            // Step 2: Check and reserve inventory (with compensation)
            .Step("reserve-inventory", async (state, ct) =>
            {
                state.Log.Add("Reserving inventory");
                foreach (var item in state.Items)
                {
                    var reserved = await inventoryService.ReserveAsync(item.ProductId, item.Quantity);
                    if (!reserved)
                    {
                        state.FailedItems.Add(item.ProductId);
                    }
                    else
                    {
                        state.ReservedItems.Add(item.ProductId);
                    }
                }
                return !state.FailedItems.Any();
            })
            .WithCompensation(async (state, ct) =>
            {
                state.Log.Add("Releasing inventory");
                foreach (var itemId in state.ReservedItems)
                {
                    await inventoryService.ReleaseAsync(itemId);
                }
            })
            // Step 3: Process payment (with retry)
            .Step("process-payment", async (state, ct) =>
            {
                state.Log.Add("Processing payment");
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        state.PaymentResult = await paymentService.ProcessAsync(state.PaymentInfo, state.TotalAmount);
                        if (state.PaymentResult.Success)
                        {
                            state.PaymentId = state.PaymentResult.TransactionId;
                            return true;
                        }
                    }
                    catch (PaymentTransientException)
                    {
                        state.Log.Add($"Payment attempt {attempt} failed, retrying...");
                        if (attempt < 3) await Task.Delay(100 * attempt, ct);
                    }
                }
                return false;
            })
            .WithCompensation(async (state, ct) =>
            {
                if (state.PaymentId != null)
                {
                    state.Log.Add("Refunding payment");
                    await paymentService.RefundAsync(state.PaymentId);
                }
            })
            // Step 4: Send notifications (parallel, continue on failure)
            .Step("send-notifications", async (state, ct) =>
            {
                state.Log.Add("Sending notifications");
                var tasks = new List<Task<bool>>
                {
                    notificationService.SendEmailAsync(state.CustomerEmail!, "Order confirmed"),
                    notificationService.SendSmsAsync(state.CustomerPhone, "Order confirmed"),
                    notificationService.SendPushAsync(state.CustomerId!, "Order confirmed")
                };

                var results = await Task.WhenAll(tasks);
                state.NotificationsSent = results.Count(r => r);
                return true; // Continue even if some notifications fail
            })
            // Step 5: Finalize
            .Step("finalize", async (state, ct) =>
            {
                state.Log.Add("Finalizing order");
                state.OrderId = $"ORD-{Guid.NewGuid():N}"[..16];
                state.Status = "Completed";
                state.CompletedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var state = new ComplexOrderState
        {
            FlowId = "complex-order-1",
            CustomerId = "CUST-001",
            CustomerEmail = "customer@example.com",
            CustomerPhone = "+1234567890",
            Items = new List<OrderItem>
            {
                new() { ProductId = "PROD-001", Quantity = 2, Price = 50m },
                new() { ProductId = "PROD-002", Quantity = 1, Price = 100m }
            },
            PaymentInfo = new PaymentInfo { CardNumber = "4111111111111111", ExpiryDate = "12/25" },
            TotalAmount = 200m
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Completed");
        result.State.OrderId.Should().NotBeNullOrEmpty();
        result.State.Log.Should().Contain("Validating order");
        result.State.Log.Should().Contain("Reserving inventory");
        result.State.Log.Should().Contain("Processing payment");
        result.State.Log.Should().Contain("Sending notifications");
        result.State.Log.Should().Contain("Finalizing order");
    }

    [Fact]
    public async Task Complex_ParallelWithCircuitBreaker_HandlesPartialFailures()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breakers = new ConcurrentDictionary<string, int>();

        var flow = FlowBuilder.Create<ParallelCircuitState>("parallel-circuit")
            .Step("init", async (state, ct) =>
            {
                state.StartTime = DateTime.UtcNow;
                return true;
            })
            .ForEach(
                s => s.Services,
                (service, f) => f.Step($"call-{service}", async (state, ct) =>
                {
                    var failureCount = breakers.GetOrAdd(service, 0);

                    if (failureCount >= 3)
                    {
                        // Circuit open - use fallback
                        state.Results[service] = $"Fallback for {service}";
                        state.FallbackUsed.Add(service);
                        return true;
                    }

                    // Simulate service call
                    if (service == "ServiceB" && state.CallAttempts[service] < 3)
                    {
                        state.CallAttempts.TryGetValue(service, out var attempts);
                        state.CallAttempts[service] = attempts + 1;
                        breakers.AddOrUpdate(service, 1, (k, v) => v + 1);
                        throw new InvalidOperationException($"{service} failed");
                    }

                    state.Results[service] = $"Success from {service}";
                    return true;
                }))
            .ContinueOnFailure()
            .Step("aggregate", async (state, ct) =>
            {
                state.EndTime = DateTime.UtcNow;
                state.Duration = state.EndTime - state.StartTime;
                return true;
            })
            .Build();

        var state = new ParallelCircuitState
        {
            FlowId = "parallel-circuit-test",
            Services = new List<string> { "ServiceA", "ServiceB", "ServiceC" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().ContainKey("ServiceA");
        result.State.Results.Should().ContainKey("ServiceC");
    }

    [Fact]
    public async Task Complex_ConcurrentWithLocking_MaintainsConsistency()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var sharedAccount = new ThreadSafeAccount(1000m);
        var results = new ConcurrentBag<FlowResult<TransferState>>();

        var flow = FlowBuilder.Create<TransferState>("concurrent-transfer")
            .Step("validate", async (state, ct) =>
            {
                state.IsValid = state.Amount > 0;
                return state.IsValid;
            })
            .Step("transfer", async (state, ct) =>
            {
                state.Success = sharedAccount.Transfer(state.Amount);
                state.NewBalance = sharedAccount.Balance;
                return true;
            })
            .Build();

        // Execute 50 concurrent transfers of $10 each
        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            var state = new TransferState { FlowId = $"transfer-{i}", Amount = 10m };
            var result = await executor.ExecuteAsync(flow, state);
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // All transfers executed
        results.Should().HaveCount(50);

        // Account should have exactly $1000 - (successful transfers * $10)
        var successfulTransfers = results.Count(r => r.State.Success);
        sharedAccount.Balance.Should().Be(1000m - (successfulTransfers * 10m));
    }

    [Fact]
    public async Task Complex_RetryWithExponentialBackoff_HandlesTransientFailures()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptTimes = new List<DateTime>();
        var failCount = 3;

        var flow = FlowBuilder.Create<RetryBackoffState>("exponential-backoff")
            .Step("operation-with-backoff", async (state, ct) =>
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    attemptTimes.Add(DateTime.UtcNow);
                    state.Attempts = attempt;

                    if (attempt <= failCount)
                    {
                        // Exponential backoff: 10ms, 20ms, 40ms...
                        var delay = (int)Math.Pow(2, attempt - 1) * 10;
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    state.Success = true;
                    return true;
                }
                return false;
            })
            .Build();

        var state = new RetryBackoffState { FlowId = "backoff-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Success.Should().BeTrue();
        result.State.Attempts.Should().Be(4); // Succeeded on 4th attempt
    }

    [Fact]
    public async Task Complex_NestedSagaWithCompensation_RollsBackCorrectly()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NestedSagaState>("nested-saga")
            // Outer transaction
            .Step("outer-begin", async (state, ct) =>
            {
                state.Log.Add("outer-begin");
                state.OuterStarted = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.Log.Add("outer-rollback");
            })
            // Inner transaction 1
            .If(s => s.ProcessInner1)
                .Then(f => f
                    .Step("inner1-begin", async (state, ct) =>
                    {
                        state.Log.Add("inner1-begin");
                        state.Inner1Started = true;
                        return true;
                    })
                    .WithCompensation(async (state, ct) =>
                    {
                        state.Log.Add("inner1-rollback");
                    })
                    .Step("inner1-work", async (state, ct) =>
                    {
                        state.Log.Add("inner1-work");
                        if (state.FailAtInner1)
                        {
                            throw new InvalidOperationException("Inner1 failure");
                        }
                        return true;
                    }))
            .EndIf()
            // Inner transaction 2
            .If(s => s.ProcessInner2)
                .Then(f => f
                    .Step("inner2-begin", async (state, ct) =>
                    {
                        state.Log.Add("inner2-begin");
                        state.Inner2Started = true;
                        return true;
                    })
                    .WithCompensation(async (state, ct) =>
                    {
                        state.Log.Add("inner2-rollback");
                    })
                    .Step("inner2-work", async (state, ct) =>
                    {
                        state.Log.Add("inner2-work");
                        if (state.FailAtInner2)
                        {
                            throw new InvalidOperationException("Inner2 failure");
                        }
                        return true;
                    }))
            .EndIf()
            // Outer commit
            .Step("outer-commit", async (state, ct) =>
            {
                state.Log.Add("outer-commit");
                state.OuterCommitted = true;
                return true;
            })
            .Build();

        // Test: Fail at inner2, should rollback inner2 and inner1 and outer
        var state = new NestedSagaState
        {
            FlowId = "nested-saga-test",
            ProcessInner1 = true,
            ProcessInner2 = true,
            FailAtInner2 = true
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Log.Should().Contain("outer-begin");
        result.State.Log.Should().Contain("inner1-begin");
        result.State.Log.Should().Contain("inner2-begin");
        result.State.Log.Should().Contain("inner2-rollback");
        result.State.Log.Should().Contain("inner1-rollback");
        result.State.Log.Should().Contain("outer-rollback");
    }

    [Fact]
    public async Task Complex_DynamicWorkflow_AdaptsToState()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DynamicWorkflowState>("dynamic-workflow")
            .Step("analyze", async (state, ct) =>
            {
                state.WorkflowType = state.DataSize switch
                {
                    < 100 => "Simple",
                    < 1000 => "Standard",
                    _ => "Complex"
                };
                return true;
            })
            .Switch(s => s.WorkflowType)
                .Case("Simple", f => f.Step("simple-process", async (state, ct) =>
                {
                    state.Steps.Add("simple-process");
                    state.ProcessingTime = 10;
                    return true;
                }))
                .Case("Standard", f => f
                    .Step("standard-validate", async (state, ct) =>
                    {
                        state.Steps.Add("standard-validate");
                        return true;
                    })
                    .Step("standard-process", async (state, ct) =>
                    {
                        state.Steps.Add("standard-process");
                        state.ProcessingTime = 50;
                        return true;
                    }))
                .Default(f => f
                    .Step("complex-validate", async (state, ct) =>
                    {
                        state.Steps.Add("complex-validate");
                        return true;
                    })
                    .Step("complex-transform", async (state, ct) =>
                    {
                        state.Steps.Add("complex-transform");
                        return true;
                    })
                    .Step("complex-process", async (state, ct) =>
                    {
                        state.Steps.Add("complex-process");
                        state.ProcessingTime = 200;
                        return true;
                    })
                    .Step("complex-verify", async (state, ct) =>
                    {
                        state.Steps.Add("complex-verify");
                        return true;
                    }))
            .EndSwitch()
            .Step("finalize", async (state, ct) =>
            {
                state.Steps.Add("finalize");
                return true;
            })
            .Build();

        // Test complex path
        var state = new DynamicWorkflowState { FlowId = "dynamic-test", DataSize = 5000 };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.WorkflowType.Should().Be("Complex");
        result.State.Steps.Should().ContainInOrder("complex-validate", "complex-transform", "complex-process", "complex-verify", "finalize");
    }

    #region State Classes and Mocks

    public class ComplexOrderState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? CustomerId { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public List<OrderItem> Items { get; set; } = new();
        public PaymentInfo? PaymentInfo { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsValid { get; set; }
        public List<string> ReservedItems { get; set; } = new();
        public List<string> FailedItems { get; set; } = new();
        public PaymentResult? PaymentResult { get; set; }
        public string? PaymentId { get; set; }
        public int NotificationsSent { get; set; }
        public string? OrderId { get; set; }
        public string? Status { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<string> Log { get; set; } = new();
    }

    public class OrderItem { public string ProductId { get; set; } = ""; public int Quantity { get; set; } public decimal Price { get; set; } }
    public class PaymentInfo { public string CardNumber { get; set; } = ""; public string ExpiryDate { get; set; } = ""; }
    public class PaymentResult { public bool Success { get; set; } public string? TransactionId { get; set; } }

    public class ParallelCircuitState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Services { get; set; } = new();
        public Dictionary<string, string> Results { get; set; } = new();
        public Dictionary<string, int> CallAttempts { get; set; } = new();
        public List<string> FallbackUsed { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class TransferState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public decimal Amount { get; set; }
        public bool IsValid { get; set; }
        public bool Success { get; set; }
        public decimal NewBalance { get; set; }
    }

    public class RetryBackoffState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Attempts { get; set; }
        public bool Success { get; set; }
    }

    public class NestedSagaState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ProcessInner1 { get; set; }
        public bool ProcessInner2 { get; set; }
        public bool FailAtInner1 { get; set; }
        public bool FailAtInner2 { get; set; }
        public bool OuterStarted { get; set; }
        public bool Inner1Started { get; set; }
        public bool Inner2Started { get; set; }
        public bool OuterCommitted { get; set; }
        public List<string> Log { get; set; } = new();
    }

    public class DynamicWorkflowState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int DataSize { get; set; }
        public string? WorkflowType { get; set; }
        public List<string> Steps { get; set; } = new();
        public int ProcessingTime { get; set; }
    }

    public class ThreadSafeAccount
    {
        private decimal _balance;
        private readonly object _lock = new();

        public ThreadSafeAccount(decimal initial) => _balance = initial;
        public decimal Balance { get { lock (_lock) return _balance; } }

        public bool Transfer(decimal amount)
        {
            lock (_lock)
            {
                if (_balance >= amount) { _balance -= amount; return true; }
                return false;
            }
        }
    }

    public class MockInventoryService
    {
        public Task<bool> ReserveAsync(string productId, int quantity) => Task.FromResult(true);
        public Task ReleaseAsync(string productId) => Task.CompletedTask;
    }

    public class MockPaymentService
    {
        public Task<PaymentResult> ProcessAsync(PaymentInfo? info, decimal amount)
            => Task.FromResult(new PaymentResult { Success = true, TransactionId = $"PAY-{Guid.NewGuid():N}"[..12] });
        public Task RefundAsync(string transactionId) => Task.CompletedTask;
    }

    public class MockNotificationService
    {
        public Task<bool> SendEmailAsync(string email, string message) => Task.FromResult(true);
        public Task<bool> SendSmsAsync(string? phone, string message) => Task.FromResult(phone != null);
        public Task<bool> SendPushAsync(string userId, string message) => Task.FromResult(true);
    }

    public class PaymentTransientException : Exception { public PaymentTransientException(string msg) : base(msg) { } }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
