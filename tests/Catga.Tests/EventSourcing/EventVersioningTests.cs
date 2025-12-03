using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.EventSourcing;

public partial class EventVersioningTests
{
    [Fact]
    public void EventVersionRegistry_RegisterAndResolve()
    {
        var registry = new EventVersionRegistry();
        var upgrader = new OrderCreatedV1ToV2Upgrader();

        registry.Register(upgrader);

        registry.HasUpgraders(typeof(OrderCreatedV1)).Should().BeTrue();
        registry.HasUpgraders(typeof(OrderCreatedV2)).Should().BeFalse();
    }

    [Fact]
    public void EventVersionRegistry_GetCurrentVersion()
    {
        var registry = new EventVersionRegistry();
        registry.Register(new OrderCreatedV1ToV2Upgrader());

        registry.GetCurrentVersion<OrderCreatedV2>().Should().Be(2);
    }

    [Fact]
    public void EventVersionRegistry_UpgradeToLatest()
    {
        var registry = new EventVersionRegistry();
        registry.Register(new OrderCreatedV1ToV2Upgrader());

        var v1Event = new OrderCreatedV1 { OrderId = "order-1", CustomerId = "customer-1" };

        var upgraded = registry.UpgradeToLatest(v1Event, 1);

        upgraded.Should().BeOfType<OrderCreatedV2>();
        var v2 = (OrderCreatedV2)upgraded;
        v2.OrderId.Should().Be("order-1");
        v2.CustomerId.Should().Be("customer-1");
        v2.CustomerName.Should().Be("Unknown"); // Default value from upgrade
    }

    [Fact]
    public void EventVersionRegistry_NoUpgrader_ReturnsOriginal()
    {
        var registry = new EventVersionRegistry();
        var evt = new OrderCreatedV2 { OrderId = "order-1", CustomerId = "customer-1", CustomerName = "John" };

        var result = registry.UpgradeToLatest(evt, 2);

        result.Should().BeSameAs(evt);
    }

    [Fact]
    public void EventVersionRegistry_ChainedUpgrades()
    {
        var registry = new EventVersionRegistry();
        registry.Register(new OrderCreatedV1ToV2Upgrader());
        registry.Register(new OrderCreatedV2ToV3Upgrader());

        var v1Event = new OrderCreatedV1 { OrderId = "order-1", CustomerId = "customer-1" };

        var upgraded = registry.UpgradeToLatest(v1Event, 1);

        upgraded.Should().BeOfType<OrderCreatedV3>();
        var v3 = (OrderCreatedV3)upgraded;
        v3.OrderId.Should().Be("order-1");
        v3.CustomerId.Should().Be("customer-1");
        v3.CustomerName.Should().Be("Unknown");
        v3.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EventVersionAttribute_StoresVersion()
    {
        var attr = new EventVersionAttribute(3);
        attr.Version.Should().Be(3);
    }

    [Fact]
    public void VersionedStoredEvent_StoresAllProperties()
    {
        var evt = new OrderCreatedV1 { OrderId = "order-1", CustomerId = "customer-1" };
        var timestamp = DateTime.UtcNow;

        var stored = new VersionedStoredEvent
        {
            Version = 5,
            Event = evt,
            Timestamp = timestamp,
            EventType = "OrderCreatedV1",
            SchemaVersion = 1
        };

        stored.Version.Should().Be(5);
        stored.Event.Should().Be(evt);
        stored.Timestamp.Should().Be(timestamp);
        stored.EventType.Should().Be("OrderCreatedV1");
        stored.SchemaVersion.Should().Be(1);
    }

    // Test event versions
    [MemoryPackable]
    private partial record OrderCreatedV1 : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
    }

    [MemoryPackable]
    private partial record OrderCreatedV2 : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
    }

    [MemoryPackable]
    private partial record OrderCreatedV3 : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }

    // Upgraders
    private class OrderCreatedV1ToV2Upgrader : EventUpgrader<OrderCreatedV1, OrderCreatedV2>
    {
        public override int SourceVersion => 1;
        public override int TargetVersion => 2;

        protected override OrderCreatedV2 UpgradeCore(OrderCreatedV1 source) => new()
        {
            MessageId = source.MessageId,
            OrderId = source.OrderId,
            CustomerId = source.CustomerId,
            CustomerName = "Unknown"
        };
    }

    private class OrderCreatedV2ToV3Upgrader : EventUpgrader<OrderCreatedV2, OrderCreatedV3>
    {
        public override int SourceVersion => 2;
        public override int TargetVersion => 3;

        protected override OrderCreatedV3 UpgradeCore(OrderCreatedV2 source) => new()
        {
            MessageId = source.MessageId,
            OrderId = source.OrderId,
            CustomerId = source.CustomerId,
            CustomerName = source.CustomerName,
            CreatedAt = DateTime.UtcNow
        };
    }
}
