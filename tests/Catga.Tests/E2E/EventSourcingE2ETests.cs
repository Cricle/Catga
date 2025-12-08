using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// End-to-end tests for event sourcing features.
/// Tests complete workflows across multiple components.
/// </summary>
public class EventSourcingE2ETests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryProjectionCheckpointStore _checkpointStore;
    private readonly InMemorySubscriptionStore _subscriptionStore;
    private readonly EnhancedInMemorySnapshotStore _snapshotStore;
    private readonly InMemoryAuditLogStore _auditStore;

    public EventSourcingE2ETests()
    {
        var provider = new DiagnosticResiliencePipelineProvider();
        _eventStore = new InMemoryEventStore(provider);
        _checkpointStore = new InMemoryProjectionCheckpointStore();
        _subscriptionStore = new InMemorySubscriptionStore();
        _snapshotStore = new EnhancedInMemorySnapshotStore();
        _auditStore = new InMemoryAuditLogStore();
    }

    [Fact]
    public async Task E2E_CompleteOrderWorkflow()
    {
        // 1. Create order events
        var orderId = "order-001";
        var streamId = $"Order-{orderId}";

        await _eventStore.AppendAsync(streamId, [
            new OrderCreated { OrderId = orderId, CustomerId = "cust-001", Amount = 0 },
            new ItemAdded { OrderId = orderId, ProductName = "Laptop", Price = 999.99m },
            new ItemAdded { OrderId = orderId, ProductName = "Mouse", Price = 29.99m },
            new OrderConfirmed { OrderId = orderId }
        ]);

        // 2. Verify events stored
        var stream = await _eventStore.ReadAsync(streamId);
        stream.Events.Should().HaveCount(4);

        // 3. Build projection
        var projection = new OrderTotalProjection();
        var rebuilder = new ProjectionRebuilder<OrderTotalProjection>(
            _eventStore, _checkpointStore, projection, "order-totals");
        await rebuilder.RebuildAsync();

        projection.Totals[orderId].Should().BeApproximately(1029.98m, 0.01m);

        // 4. Create snapshot
        var aggregate = new TestOrderAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        await _snapshotStore.SaveAsync(streamId, aggregate, stream.Version);

        // 5. Verify snapshot
        var snapshot = await _snapshotStore.LoadAsync<TestOrderAggregate>(streamId);
        snapshot.HasValue.Should().BeTrue();
        snapshot.Value.State.TotalAmount.Should().BeApproximately(1029.98m, 0.01m);

        // 6. Verify stream integrity
        var verifier = new ImmutabilityVerifier(_eventStore);
        var verification = await verifier.VerifyStreamAsync(streamId);
        verification.IsValid.Should().BeTrue();

        // 7. Log audit
        await _auditStore.LogAsync(new AuditLogEntry
        {
            StreamId = streamId,
            Action = AuditAction.StreamRead,
            UserId = "admin",
            Details = "Order verified"
        });

        var logs = await _auditStore.GetLogsAsync(streamId);
        logs.Should().HaveCount(1);
    }

    [Fact]
    public async Task E2E_SubscriptionProcessing()
    {
        // 1. Create multiple order streams
        await _eventStore.AppendAsync("Order-001", [new OrderCreated { OrderId = "001", CustomerId = "c1", Amount = 100 }]);
        await _eventStore.AppendAsync("Order-002", [new OrderCreated { OrderId = "002", CustomerId = "c2", Amount = 200 }]);
        await _eventStore.AppendAsync("Customer-001", [new CustomerRegistered { CustomerId = "c1" }]);

        // 2. Create subscription for orders only
        var sub = new PersistentSubscription("order-processor", "Order-*");
        await _subscriptionStore.SaveAsync(sub);

        // 3. Process subscription
        var handler = new CountingHandler();
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, handler);
        await runner.RunOnceAsync("order-processor");

        // 4. Verify only order events processed
        handler.Count.Should().Be(2);

        // 5. Verify subscription state updated
        var updated = await _subscriptionStore.LoadAsync("order-processor");
        updated!.ProcessedCount.Should().Be(2);
    }

    [Fact]
    public async Task E2E_TimeTravelWithSnapshots()
    {
        // 1. Create events over time
        var streamId = "Order-time-travel";

        await _eventStore.AppendAsync(streamId, [
            new OrderCreated { OrderId = "tt", CustomerId = "c1", Amount = 0 }
        ]);
        await _snapshotStore.SaveAsync(streamId, new TestOrderAggregate { TotalAmount = 0 }, 0);

        await _eventStore.AppendAsync(streamId, [
            new ItemAdded { OrderId = "tt", ProductName = "Item1", Price = 100 }
        ]);
        await _snapshotStore.SaveAsync(streamId, new TestOrderAggregate { TotalAmount = 100 }, 1);

        await _eventStore.AppendAsync(streamId, [
            new ItemAdded { OrderId = "tt", ProductName = "Item2", Price = 200 }
        ]);
        await _snapshotStore.SaveAsync(streamId, new TestOrderAggregate { TotalAmount = 300 }, 2);

        // 2. Time travel to different versions
        var v0 = await _snapshotStore.LoadAtVersionAsync<TestOrderAggregate>(streamId, 0);
        var v1 = await _snapshotStore.LoadAtVersionAsync<TestOrderAggregate>(streamId, 1);
        var v2 = await _snapshotStore.LoadAtVersionAsync<TestOrderAggregate>(streamId, 2);

        v0.Value.State.TotalAmount.Should().Be(0);
        v1.Value.State.TotalAmount.Should().Be(100);
        v2.Value.State.TotalAmount.Should().Be(300);

        // 3. Verify snapshot history
        var history = await _snapshotStore.GetSnapshotHistoryAsync(streamId);
        history.Should().HaveCount(3);
    }

    [Fact]
    public async Task E2E_ProjectionRebuildAfterNewEvents()
    {
        // 1. Initial events
        await _eventStore.AppendAsync("Order-001", [
            new OrderCreated { OrderId = "001", CustomerId = "c1", Amount = 100 }
        ]);

        // 2. Build projection
        var projection = new OrderCountProjection();
        var rebuilder = new ProjectionRebuilder<OrderCountProjection>(
            _eventStore, _checkpointStore, projection, "order-count");
        await rebuilder.RebuildAsync();

        projection.Count.Should().Be(1);

        // 3. Add more events
        await _eventStore.AppendAsync("Order-002", [
            new OrderCreated { OrderId = "002", CustomerId = "c2", Amount = 200 }
        ]);
        await _eventStore.AppendAsync("Order-003", [
            new OrderCreated { OrderId = "003", CustomerId = "c3", Amount = 300 }
        ]);

        // 4. Rebuild projection
        await rebuilder.RebuildAsync();

        projection.Count.Should().Be(3);
    }

    [Fact]
    public async Task E2E_AuditTrailForCompliance()
    {
        // 1. Perform operations and log them
        var streamId = "Order-audit-test";

        await _eventStore.AppendAsync(streamId, [new OrderCreated { OrderId = "audit", CustomerId = "c1", Amount = 100 }]);
        await _auditStore.LogAsync(new AuditLogEntry { StreamId = streamId, Action = AuditAction.EventAppended, UserId = "user1" });

        await _eventStore.AppendAsync(streamId, [new ItemAdded { OrderId = "audit", ProductName = "Item", Price = 50 }]);
        await _auditStore.LogAsync(new AuditLogEntry { StreamId = streamId, Action = AuditAction.EventAppended, UserId = "user1" });

        // Read operation
        await _eventStore.ReadAsync(streamId);
        await _auditStore.LogAsync(new AuditLogEntry { StreamId = streamId, Action = AuditAction.StreamRead, UserId = "user2" });

        // 2. Query audit logs
        var logs = await _auditStore.GetLogsAsync(streamId);
        logs.Should().HaveCount(3);
        logs.Should().Contain(l => l.Action == AuditAction.EventAppended);
        logs.Should().Contain(l => l.Action == AuditAction.StreamRead);

        // 3. Query by time range
        var recentLogs = await _auditStore.GetLogsByTimeRangeAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));
        recentLogs.Should().HaveCount(3);
    }

    #region Test events and projections

    private record OrderCreated : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
        public string CustomerId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    private record ItemAdded : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
        public string ProductName { get; init; } = "";
        public decimal Price { get; init; }
    }

    private record OrderConfirmed : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
    }

    private record CustomerRegistered : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string CustomerId { get; init; } = "";
    }

    private class TestOrderAggregate : AggregateRoot
    {
        private string _id = "";
        public override string Id { get => _id; protected set => _id = value; }
        public decimal TotalAmount { get; set; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case OrderCreated e:
                    _id = e.OrderId;
                    TotalAmount = e.Amount;
                    break;
                case ItemAdded e:
                    TotalAmount += e.Price;
                    break;
            }
        }
    }

    private class OrderTotalProjection : IProjection
    {
        public string Name => "order-totals";
        public Dictionary<string, decimal> Totals { get; } = new();

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            switch (@event)
            {
                case OrderCreated e:
                    Totals[e.OrderId] = e.Amount;
                    break;
                case ItemAdded e:
                    if (Totals.ContainsKey(e.OrderId))
                        Totals[e.OrderId] += e.Price;
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Totals.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private class OrderCountProjection : IProjection
    {
        public string Name => "order-count";
        public int Count { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is OrderCreated) Count++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Count = 0;
            return ValueTask.CompletedTask;
        }
    }

    private class CountingHandler : IEventHandler
    {
        public int Count { get; private set; }

        public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}






