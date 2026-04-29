using Catga.Abstractions;
using Catga.Messaging;
using Catga.Transport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Transport;

// ── Domain types ──────────────────────────────────────────────────────────────

// V1 message (old)
public record OrderCreatedV1(string OrderId, string CustomerId) : IEvent
{
    public long MessageId { get; init; }
}

// V2 message (new - added Amount field)
[MessageVersion(2)]
public record OrderCreatedV2(string OrderId, string CustomerId, decimal Amount) : IEvent
{
    public long MessageId { get; init; }
}

// V3 message (renamed field)
[MessageVersion(3)]
public record OrderCreatedV3(string OrderId, string BuyerId, decimal Amount) : IEvent
{
    public long MessageId { get; init; }
}

// Renamed message (was OrderPlaced, now OrderCreatedV2)
public record OrderPlacedLegacy(string OrderId) : IEvent
{
    public long MessageId { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// IMessageVersionMapper UNIT TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageVersionMapperTests
{
    [Fact]
    public void AddAlias_ResolvesOldNameToNewType()
    {
        var mapper = new MessageVersionMapper();
        mapper.AddAlias("OrderPlaced", typeof(OrderCreatedV2));

        mapper.ResolveType("OrderPlaced").Should().Be(typeof(OrderCreatedV2));
    }

    [Fact]
    public void ResolveType_UnknownName_ReturnsNull()
    {
        var mapper = new MessageVersionMapper();
        mapper.ResolveType("UnknownMessage").Should().BeNull();
    }

    [Fact]
    public void ResolveType_CaseInsensitive()
    {
        var mapper = new MessageVersionMapper();
        mapper.AddAlias("orderplaced", typeof(OrderCreatedV2));

        mapper.ResolveType("OrderPlaced").Should().Be(typeof(OrderCreatedV2));
        mapper.ResolveType("ORDERPLACED").Should().Be(typeof(OrderCreatedV2));
    }

    [Fact]
    public void AddUpgrader_V1ToV2_UpgradesContent()
    {
        var mapper = new MessageVersionMapper();
        mapper.AddUpgrader<OrderCreatedV1, OrderCreatedV2>(
            v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, Amount: 0m));

        var v1 = new OrderCreatedV1("ORD-1", "CUST-1");
        var upgraded = mapper.Upgrade(v1);

        upgraded.Should().BeOfType<OrderCreatedV2>();
        var v2 = (OrderCreatedV2)upgraded;
        v2.OrderId.Should().Be("ORD-1");
        v2.CustomerId.Should().Be("CUST-1");
        v2.Amount.Should().Be(0m);
    }

    [Fact]
    public void Upgrade_ChainedUpgraders_V1ToV3()
    {
        var mapper = new MessageVersionMapper();
        mapper.AddUpgrader<OrderCreatedV1, OrderCreatedV2>(
            v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m));
        mapper.AddUpgrader<OrderCreatedV2, OrderCreatedV3>(
            v2 => new OrderCreatedV3(v2.OrderId, BuyerId: v2.CustomerId, v2.Amount));

        var v1 = new OrderCreatedV1("ORD-1", "CUST-1");
        var upgraded = mapper.Upgrade(v1);

        upgraded.Should().BeOfType<OrderCreatedV3>();
        var v3 = (OrderCreatedV3)upgraded;
        v3.BuyerId.Should().Be("CUST-1");
    }

    [Fact]
    public void Upgrade_NoUpgrader_ReturnsSameInstance()
    {
        var mapper = new MessageVersionMapper();
        var msg = new OrderCreatedV2("ORD-1", "CUST-1", 100m);

        var result = mapper.Upgrade(msg);

        result.Should().BeSameAs(msg);
    }

    [Fact]
    public void Upgrade_AlreadyLatestVersion_ReturnsSameInstance()
    {
        var mapper = new MessageVersionMapper();
        mapper.AddUpgrader<OrderCreatedV1, OrderCreatedV2>(
            v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m));

        var v2 = new OrderCreatedV2("ORD-1", "CUST-1", 100m);
        var result = mapper.Upgrade(v2);

        result.Should().BeSameAs(v2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// MessageVersionMapperBuilder TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageVersionMapperBuilderTests
{
    [Fact]
    public void Builder_MapType_RegistersAlias()
    {
        var mapper = new MessageVersionMapperBuilder()
            .MapType("OrderPlaced", typeof(OrderCreatedV2))
            .Build();

        mapper.ResolveType("OrderPlaced").Should().Be(typeof(OrderCreatedV2));
    }

    [Fact]
    public void Builder_Upgrade_RegistersUpgrader()
    {
        var mapper = new MessageVersionMapperBuilder()
            .Upgrade<OrderCreatedV1, OrderCreatedV2>(
                v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m))
            .Build();

        var v1 = new OrderCreatedV1("ORD-1", "CUST-1");
        mapper.Upgrade(v1).Should().BeOfType<OrderCreatedV2>();
    }

    [Fact]
    public void Builder_ChainedConfig_AllRegistered()
    {
        var mapper = new MessageVersionMapperBuilder()
            .MapType("OrderPlaced", typeof(OrderCreatedV2))
            .MapType("LegacyOrder", typeof(OrderCreatedV1))
            .Upgrade<OrderCreatedV1, OrderCreatedV2>(
                v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m))
            .Build();

        mapper.ResolveType("OrderPlaced").Should().Be(typeof(OrderCreatedV2));
        mapper.ResolveType("LegacyOrder").Should().Be(typeof(OrderCreatedV1));
        mapper.Upgrade(new OrderCreatedV1("x", "y")).Should().BeOfType<OrderCreatedV2>();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// MessageVersionAttribute TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageVersionAttributeTests
{
    [Fact]
    public void MessageVersionAttribute_SetsVersion()
    {
        var attr = new MessageVersionAttribute(3);
        attr.Version.Should().Be(3);
    }

    [Fact]
    public void OrderCreatedV2_HasVersion2Attribute()
    {
        var attr = typeof(OrderCreatedV2)
            .GetCustomAttributes(typeof(MessageVersionAttribute), false)
            .OfType<MessageVersionAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull();
        attr!.Version.Should().Be(2);
    }

    [Fact]
    public void OrderCreatedV1_HasNoVersionAttribute_DefaultsTo1()
    {
        var attr = typeof(OrderCreatedV1)
            .GetCustomAttributes(typeof(MessageVersionAttribute), false)
            .OfType<MessageVersionAttribute>()
            .FirstOrDefault();

        attr.Should().BeNull("V1 has no attribute, defaults to version 1");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// DI REGISTRATION TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageVersioningDiTests
{
    [Fact]
    public void WithMessageVersioning_RegistersIMessageVersionMapper()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddCatga().WithMessageVersioning(b =>
            b.MapType("OrderPlaced", typeof(OrderCreatedV2))
             .Upgrade<OrderCreatedV1, OrderCreatedV2>(
                 v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m)));

        var sp = services.BuildServiceProvider();
        var mapper = sp.GetService<IMessageVersionMapper>();
        mapper.Should().NotBeNull();
    }

    [Fact]
    public void WithMessageVersioning_MapperHasRegistrations()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddCatga().WithMessageVersioning(b =>
            b.MapType("OldOrderCreated", typeof(OrderCreatedV2)));

        var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMessageVersionMapper>();

        mapper.ResolveType("OldOrderCreated").Should().Be(typeof(OrderCreatedV2));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E: ROLLING UPGRADE SCENARIO
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageVersioningE2ETests
{
    [Fact]
    public void RollingUpgrade_V1MessageArrivesOnV2Consumer_UpgradedCorrectly()
    {
        // Scenario: Old producer sends V1, new consumer expects V2
        var mapper = new MessageVersionMapperBuilder()
            .Upgrade<OrderCreatedV1, OrderCreatedV2>(
                v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, Amount: 0m))
            .Build();

        // Simulate receiving a V1 message
        IEvent received = new OrderCreatedV1("ORD-001", "CUST-001");

        // Consumer upgrades before processing
        var upgraded = mapper.Upgrade(received);

        upgraded.Should().BeOfType<OrderCreatedV2>();
        var v2 = (OrderCreatedV2)upgraded;
        v2.OrderId.Should().Be("ORD-001");
        v2.CustomerId.Should().Be("CUST-001");
        v2.Amount.Should().Be(0m);
    }

    [Fact]
    public void RollingUpgrade_TypeRename_OldNameResolvesToNewType()
    {
        // Scenario: Message was renamed from "OrderPlaced" to "OrderCreatedV2"
        var mapper = new MessageVersionMapperBuilder()
            .MapType("Catga.Tests.Transport.OrderPlacedLegacy", typeof(OrderCreatedV2))
            .Build();

        var oldTypeName = typeof(OrderPlacedLegacy).FullName!;
        var resolvedType = mapper.ResolveType(oldTypeName);

        resolvedType.Should().Be(typeof(OrderCreatedV2));
    }

    [Fact]
    public void RollingUpgrade_MultipleVersions_FullChain()
    {
        // V1 → V2 → V3 upgrade chain
        var mapper = new MessageVersionMapperBuilder()
            .Upgrade<OrderCreatedV1, OrderCreatedV2>(
                v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, 0m))
            .Upgrade<OrderCreatedV2, OrderCreatedV3>(
                v2 => new OrderCreatedV3(v2.OrderId, BuyerId: v2.CustomerId, v2.Amount))
            .Build();

        IEvent v1Message = new OrderCreatedV1("ORD-1", "CUST-1");
        var result = mapper.Upgrade(v1Message);

        result.Should().BeOfType<OrderCreatedV3>();
        var v3 = (OrderCreatedV3)result;
        v3.OrderId.Should().Be("ORD-1");
        v3.BuyerId.Should().Be("CUST-1");
    }

    [Fact]
    public void RollingUpgrade_NewMessageOnOldConsumer_PassesThrough()
    {
        // Old consumer has no upgrader for V2 → passes through unchanged
        var mapper = new MessageVersionMapperBuilder().Build();

        IEvent v2Message = new OrderCreatedV2("ORD-1", "CUST-1", 100m);
        var result = mapper.Upgrade(v2Message);

        result.Should().BeSameAs(v2Message);
    }

    [Fact]
    public void SchemaVersion_InTransportContext_ReflectsMessageVersion()
    {
        // TransportContext carries schema version for receivers to know what version arrived
        var ctx = new Catga.Transport.TransportContext
        {
            MessageType = "OrderCreatedV2",
            SchemaVersion = 2
        };

        ctx.SchemaVersion.Should().Be(2);
        ctx.MessageType.Should().Be("OrderCreatedV2");
    }
}
