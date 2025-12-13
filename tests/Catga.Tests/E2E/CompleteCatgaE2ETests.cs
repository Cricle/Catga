using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Flow.Dsl;
using Catga.Pipeline;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Complete end-to-end tests for Catga framework.
/// Tests the full integration of all components working together.
/// </summary>
public class CompleteCatgaE2ETests
{
    [Fact]
    public async Task CompleteWorkflow_CqrsWithEventSourcing_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        // Register test handlers
        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
        services.AddSingleton<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var eventStore = sp.GetRequiredService<IEventStore>();

        // Act - Create order via mediator
        var command = new CreateOrderCommand("CUST-001", 99.99m);
        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.OrderId.Should().NotBeNullOrEmpty();

        // Verify event was stored
        var streamId = $"Order-{result.Value.OrderId}";
        var stream = await eventStore.ReadAsync(streamId);
        stream.Events.Should().HaveCount(1);
        stream.Events[0].Event.Should().BeOfType<OrderCreatedEvent>();
    }

    [Fact]
    public async Task CompleteWorkflow_FlowWithPersistence_CanResumeAfterFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flowStore = sp.GetRequiredService<IDslFlowStore>();

        var stepExecuted = new List<string>();
        var shouldFail = true;

        var flow = FlowBuilder.Create<TestFlowState>("resumable-flow")
            .Step("step-1", async (state, ct) =>
            {
                stepExecuted.Add("step-1");
                state.Step1Completed = true;
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                stepExecuted.Add("step-2");
                if (shouldFail)
                {
                    shouldFail = false;
                    throw new InvalidOperationException("Simulated failure");
                }
                state.Step2Completed = true;
                return true;
            })
            .Step("step-3", async (state, ct) =>
            {
                stepExecuted.Add("step-3");
                state.Step3Completed = true;
                return true;
            })
            .Build();

        var initialState = new TestFlowState { FlowId = $"flow-{Guid.NewGuid():N}" };

        // Act - First execution (fails at step-2)
        var result1 = await executor.ExecuteAsync(flow, initialState);

        // Assert - First execution failed
        result1.IsSuccess.Should().BeFalse();
        stepExecuted.Should().Contain("step-1");
        stepExecuted.Should().Contain("step-2");
        stepExecuted.Should().NotContain("step-3");

        // Act - Resume execution (should continue from step-2)
        stepExecuted.Clear();
        var result2 = await executor.ResumeAsync(flow, initialState.FlowId);

        // Assert - Second execution succeeded
        result2.IsSuccess.Should().BeTrue();
        result2.State.Step1Completed.Should().BeTrue();
        result2.State.Step2Completed.Should().BeTrue();
        result2.State.Step3Completed.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteWorkflow_ProjectionWithSubscription_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<TestOrderProjection>();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var subscriptionStore = sp.GetRequiredService<ISubscriptionStore>();
        var projection = sp.GetRequiredService<TestOrderProjection>();

        // Create subscription
        await subscriptionStore.SaveAsync(new PersistentSubscription("order-projection", "Order-*"));

        // Append events
        var orderId = $"order-{Guid.NewGuid():N}"[..16];
        var streamId = $"Order-{orderId}";
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent { OrderId = orderId, CustomerId = "CUST-001", Amount = 100m },
            new OrderUpdatedEvent { OrderId = orderId, NewAmount = 150m }
        });

        // Act - Process subscription
        var subscription = await subscriptionStore.LoadAsync("order-projection");
        var stream = await eventStore.ReadAsync(streamId);
        foreach (var eventEnvelope in stream.Events)
        {
            await projection.ApplyAsync(eventEnvelope.Event);
        }

        // Update subscription position
        subscription = subscription! with { Position = stream.Version, ProcessedCount = stream.Events.Count };
        await subscriptionStore.SaveAsync(subscription);

        // Assert
        projection.Orders.Should().ContainKey(orderId);
        projection.Orders[orderId].Amount.Should().Be(150m);

        var updatedSub = await subscriptionStore.LoadAsync("order-projection");
        updatedSub!.ProcessedCount.Should().Be(2);
    }

    [Fact]
    public async Task CompleteWorkflow_AuditAndCompliance_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var auditStore = sp.GetRequiredService<IAuditLogStore>();
        var gdprStore = sp.GetRequiredService<IGdprStore>();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = "ORD-001";
        var streamId = $"Order-{orderId}";
        var customerId = "CUST-001";

        // Act - Append events
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent { OrderId = orderId, CustomerId = customerId, Amount = 100m }
        });

        // Act - Log audit entry
        await auditStore.LogAsync(new AuditLogEntry(
            streamId, "CreateOrder", "admin", DateTime.UtcNow,
            new Dictionary<string, object> { ["customerId"] = customerId }));

        // Act - Request GDPR erasure
        await gdprStore.SaveRequestAsync(new ErasureRequest(customerId, "customer@example.com", DateTime.UtcNow));

        // Assert - Verify audit
        var auditLogs = await auditStore.GetLogsAsync(streamId);
        auditLogs.Should().HaveCount(1);
        auditLogs[0].Operation.Should().Be("CreateOrder");

        // Assert - Verify GDPR request
        var erasureRequest = await gdprStore.GetErasureRequestAsync(customerId);
        erasureRequest.Should().NotBeNull();
        erasureRequest!.SubjectId.Should().Be(customerId);

        var pendingRequests = await gdprStore.GetPendingRequestsAsync();
        pendingRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task CompleteWorkflow_SnapshotOptimization_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var orderId = "ORD-SNAP-001";
        var streamId = $"Order-{orderId}";

        // Act - Append many events
        for (int i = 0; i < 10; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderUpdatedEvent { OrderId = orderId, NewAmount = 100m + i * 10 }
            });
        }

        // Create snapshot
        var aggregate = new TestOrderAggregate { Id = orderId, Amount = 190m, Version = 10 };
        await snapshotStore.SaveAsync(streamId, aggregate, 10);

        // Assert - Snapshot saved
        var snapshot = await snapshotStore.GetAsync<TestOrderAggregate>(streamId);
        snapshot.Should().NotBeNull();
        snapshot!.Amount.Should().Be(190m);

        // Verify snapshot history
        var history = await snapshotStore.GetSnapshotHistoryAsync(streamId);
        history.Should().HaveCountGreaterOrEqualTo(1);
    }

    #region Test Types

    public record CreateOrderCommand(string CustomerId, decimal Amount) : IRequest<OrderResult>;
    public record OrderResult(string OrderId, decimal Amount);

    public record OrderCreatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string CustomerId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record OrderUpdatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal NewAmount { get; init; }
    }

    public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
    {
        private readonly IEventStore _eventStore;
        public CreateOrderHandler(IEventStore eventStore) => _eventStore = eventStore;

        public async ValueTask<CatgaResult<OrderResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
        {
            var orderId = $"ORD-{Guid.NewGuid():N}"[..12];
            var @event = new OrderCreatedEvent { OrderId = orderId, CustomerId = request.CustomerId, Amount = request.Amount };
            await _eventStore.AppendAsync($"Order-{orderId}", new[] { @event }, ct);
            return CatgaResult<OrderResult>.Success(new OrderResult(orderId, request.Amount));
        }
    }

    public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
    {
        public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
        {
            // Log or process event
            return ValueTask.CompletedTask;
        }
    }

    public class TestFlowState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool Step1Completed { get; set; }
        public bool Step2Completed { get; set; }
        public bool Step3Completed { get; set; }
    }

    public class TestOrderProjection : IProjection
    {
        public string Name => "TestOrderProjection";
        public Dictionary<string, OrderReadModel> Orders { get; } = new();

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    Orders[e.OrderId] = new OrderReadModel { OrderId = e.OrderId, CustomerId = e.CustomerId, Amount = e.Amount };
                    break;
                case OrderUpdatedEvent e:
                    if (Orders.TryGetValue(e.OrderId, out var order))
                        order.Amount = e.NewAmount;
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Orders.Clear();
            return ValueTask.CompletedTask;
        }
    }

    public class OrderReadModel
    {
        public string OrderId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class TestOrderAggregate
    {
        public string Id { get; set; } = "";
        public decimal Amount { get; set; }
        public long Version { get; set; }
    }

    #endregion
}
