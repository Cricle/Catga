using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.EventSourcing;

public partial class AggregateRootTests
{
    [Fact]
    public void AggregateRoot_InitialState()
    {
        var aggregate = new TestOrderAggregate();

        aggregate.Version.Should().Be(0);
        aggregate.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void AggregateRoot_RaiseEvent_IncrementsVersion()
    {
        var aggregate = new TestOrderAggregate();

        aggregate.Create("order-1", "customer-1");

        aggregate.Version.Should().Be(1);
        aggregate.Id.Should().Be("order-1");
        aggregate.CustomerId.Should().Be("customer-1");
    }

    [Fact]
    public void AggregateRoot_RaiseEvent_AddsToUncommittedEvents()
    {
        var aggregate = new TestOrderAggregate();

        aggregate.Create("order-1", "customer-1");

        aggregate.UncommittedEvents.Should().HaveCount(1);
        aggregate.UncommittedEvents[0].Should().BeOfType<OrderCreatedEvent>();
    }

    [Fact]
    public void AggregateRoot_ClearUncommittedEvents_ClearsEvents()
    {
        var aggregate = new TestOrderAggregate();
        aggregate.Create("order-1", "customer-1");

        aggregate.ClearUncommittedEvents();

        aggregate.UncommittedEvents.Should().BeEmpty();
        aggregate.Version.Should().Be(1); // Version unchanged
    }

    [Fact]
    public void AggregateRoot_LoadFromHistory_AppliesEvents()
    {
        var aggregate = new TestOrderAggregate();
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-1", CustomerId = "customer-1" },
            new OrderConfirmedEvent { OrderId = "order-1" }
        };

        aggregate.LoadFromHistory(events);

        aggregate.Version.Should().Be(2);
        aggregate.Id.Should().Be("order-1");
        aggregate.CustomerId.Should().Be("customer-1");
        aggregate.IsConfirmed.Should().BeTrue();
        aggregate.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void AggregateRoot_MultipleEvents_TracksAllUncommitted()
    {
        var aggregate = new TestOrderAggregate();

        aggregate.Create("order-1", "customer-1");
        aggregate.Confirm();

        aggregate.Version.Should().Be(2);
        aggregate.UncommittedEvents.Should().HaveCount(2);
        aggregate.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void AggregateRoot_Apply_UpdatesStateWithoutAddingToUncommitted()
    {
        var aggregate = new TestOrderAggregate();
        var evt = new OrderCreatedEvent { OrderId = "order-1", CustomerId = "customer-1" };

        aggregate.Apply(evt);

        aggregate.Version.Should().Be(1);
        aggregate.Id.Should().Be("order-1");
        aggregate.UncommittedEvents.Should().BeEmpty();
    }

    // Test aggregate implementation
    private class TestOrderAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = string.Empty;
        public string CustomerId { get; private set; } = string.Empty;
        public bool IsConfirmed { get; private set; }

        public void Create(string orderId, string customerId)
        {
            RaiseEvent(new OrderCreatedEvent { OrderId = orderId, CustomerId = customerId });
        }

        public void Confirm()
        {
            RaiseEvent(new OrderConfirmedEvent { OrderId = Id });
        }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    Id = e.OrderId;
                    CustomerId = e.CustomerId;
                    break;
                case OrderConfirmedEvent:
                    IsConfirmed = true;
                    break;
            }
        }
    }

    [MemoryPackable]
    private partial record OrderCreatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
    }

    [MemoryPackable]
    private partial record OrderConfirmedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = string.Empty;
    }
}
