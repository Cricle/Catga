using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Persistence;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class EventSourcingE2ETests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _nats;
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        // NATS with JetStream
        var natsImage = Environment.GetEnvironmentVariable("TEST_NATS_IMAGE") ?? "nats:2.10-alpine";
        if (!IsImageAvailable(natsImage)) return;
        _natsContainer = new ContainerBuilder()
            .WithImage(natsImage)
            .WithPortBinding(4222, true)
            .WithCommand("-js")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();
        await _natsContainer.StartAsync();
        var natsPort = _natsContainer.GetMappedPublicPort(4222);
        _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{natsPort}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await ConnectWithRetryAsync(_nats);

        // Redis
        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder().WithImage(redisImage).Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        _redis?.Dispose();
        if (_natsContainer is not null) await _natsContainer.DisposeAsync();
        if (_redisContainer is not null) await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool IsImageAvailable(string image)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"image inspect {image}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task ConnectWithRetryAsync(NatsConnection connection, int attempts = 10, int delayMs = 500)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                await connection.ConnectAsync();
                return;
            }
            catch
            {
                if (i == attempts - 1) throw;
                await Task.Delay(delayMs);
            }
        }
    }

    [Fact]
    public async Task NATS_EventStore_AppendAndRead()
    {
        if (_nats is null) return;

        var provider = new DiagnosticResiliencePipelineProvider();
        var streamName = $"ES_TEST_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(_nats, _serializer, streamName, options: null, provider: provider);

        var streamId = $"order-{Guid.NewGuid()}";

        // Append events
        var events = new List<IEvent>
        {
            new OrderCreatedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId, CustomerId = "c1", Amount = 100m },
            new OrderItemAddedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId, ProductId = "p1", Quantity = 2 },
            new OrderConfirmedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId }
        };

        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(200);

        // Read back
        var stream = await eventStore.ReadAsync(streamId);
        stream.Events.Should().HaveCountGreaterOrEqualTo(3);

        // Verify version
        var version = await eventStore.GetVersionAsync(streamId);
        version.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task Redis_SnapshotStore_WithEventStore_Integration()
    {
        if (_nats is null || _redis is null) return;

        var provider = new DiagnosticResiliencePipelineProvider();
        var streamName = $"ES_SNAP_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(_nats, _serializer, streamName, options: null, provider: provider);
        var snapshotStore = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);

        var streamId = $"order-{Guid.NewGuid()}";

        // Append initial events
        var events = new List<IEvent>
        {
            new OrderCreatedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId, CustomerId = "c1", Amount = 50m },
            new OrderItemAddedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId, ProductId = "p1", Quantity = 1 }
        };
        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(200);

        // Create snapshot
        var state = new OrderState { OrderId = streamId, CustomerId = "c1", TotalAmount = 50m, ItemCount = 1 };
        await snapshotStore.SaveAsync(streamId, state, version: 1);

        // Append more events
        var moreEvents = new List<IEvent>
        {
            new OrderItemAddedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId, ProductId = "p2", Quantity = 3 },
            new OrderConfirmedEvent { MessageId = MessageExtensions.NewMessageId(), OrderId = streamId }
        };
        await eventStore.AppendAsync(streamId, moreEvents);
        await Task.Delay(200);

        // Load snapshot and replay events after snapshot
        var snapshot = await snapshotStore.LoadAsync<OrderState>(streamId);
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(1);
        snapshot.Value.State.ItemCount.Should().Be(1);

        // Read events after snapshot version
        var stream = await eventStore.ReadAsync(streamId, fromVersion: snapshot.Value.Version + 1);
        stream.Events.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void EventVersionRegistry_UpgradeChain()
    {
        var registry = new EventVersionRegistry();

        // Register upgraders
        registry.Register(new OrderCreatedV1ToV2Upgrader());
        registry.Register(new OrderCreatedV2ToV3Upgrader());

        // Create V1 event
        var v1Event = new OrderCreatedEventV1 { MessageId = MessageExtensions.NewMessageId(), OrderId = "o1", Customer = "c1" };

        // Upgrade to latest (fromVersion = 1)
        var upgraded = registry.UpgradeToLatest(v1Event, fromVersion: 1);

        upgraded.Should().BeOfType<OrderCreatedEventV3>();
        var v3 = (OrderCreatedEventV3)upgraded;
        v3.OrderId.Should().Be("o1");
        v3.CustomerId.Should().Be("c1");
        v3.Version.Should().Be(3);
    }

    [Fact]
    public void AggregateRoot_ApplyEventsAndRebuild()
    {
        var aggregate = new OrderAggregate();

        // Apply events
        aggregate.Create("order-1", "customer-1");
        aggregate.AddItem("product-1", 2, 25.00m);
        aggregate.AddItem("product-2", 1, 50.00m);
        aggregate.Confirm();

        // Verify state
        aggregate.Id.Should().Be("order-1");
        aggregate.CustomerId.Should().Be("customer-1");
        aggregate.TotalAmount.Should().Be(100.00m);
        aggregate.ItemCount.Should().Be(3);
        aggregate.IsConfirmed.Should().BeTrue();
        aggregate.Version.Should().Be(4);

        // Get uncommitted events
        var uncommitted = aggregate.UncommittedEvents.ToList();
        uncommitted.Should().HaveCount(4);

        // Clear and rebuild from history
        var newAggregate = new OrderAggregate();
        newAggregate.LoadFromHistory(uncommitted);

        newAggregate.Id.Should().Be("order-1");
        newAggregate.CustomerId.Should().Be("customer-1");
        newAggregate.TotalAmount.Should().Be(100.00m);
        newAggregate.ItemCount.Should().Be(3);
        newAggregate.IsConfirmed.Should().BeTrue();
    }

    #region Test Events

    [MemoryPackable]
    private partial record OrderCreatedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; }
        public required decimal Amount { get; init; }
    }

    [MemoryPackable]
    private partial record OrderItemAddedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
    }

    [MemoryPackable]
    private partial record OrderConfirmedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
    }

    [MemoryPackable]
    private partial class OrderState
    {
        public string OrderId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
    }

    // Event versioning types
    [EventVersion(1)]
    [MemoryPackable]
    private partial record OrderCreatedEventV1 : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string Customer { get; init; } // Old field name
    }

    [EventVersion(2)]
    [MemoryPackable]
    private partial record OrderCreatedEventV2 : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; } // Renamed field
    }

    [EventVersion(3)]
    [MemoryPackable]
    private partial record OrderCreatedEventV3 : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; }
        public int Version { get; init; } = 3; // Added field
    }

    private sealed class OrderCreatedV1ToV2Upgrader : EventUpgrader<OrderCreatedEventV1, OrderCreatedEventV2>
    {
        public override int SourceVersion => 1;
        public override int TargetVersion => 2;
        protected override OrderCreatedEventV2 UpgradeCore(OrderCreatedEventV1 source)
            => new() { MessageId = source.MessageId, OrderId = source.OrderId, CustomerId = source.Customer };
    }

    private sealed class OrderCreatedV2ToV3Upgrader : EventUpgrader<OrderCreatedEventV2, OrderCreatedEventV3>
    {
        public override int SourceVersion => 2;
        public override int TargetVersion => 3;
        protected override OrderCreatedEventV3 UpgradeCore(OrderCreatedEventV2 source)
            => new() { MessageId = source.MessageId, OrderId = source.OrderId, CustomerId = source.CustomerId, Version = 3 };
    }

    // Aggregate Root
    private sealed class OrderAggregate : AggregateRoot
    {
        private string _id = string.Empty;
        public override string Id { get => _id; protected set => _id = value; }
        public string CustomerId { get; private set; } = string.Empty;
        public decimal TotalAmount { get; private set; }
        public int ItemCount { get; private set; }
        public bool IsConfirmed { get; private set; }

        public void Create(string orderId, string customerId)
        {
            RaiseEvent(new AggregateOrderCreated { MessageId = MessageExtensions.NewMessageId(), OrderId = orderId, CustomerId = customerId });
        }

        public void AddItem(string productId, int quantity, decimal price)
        {
            RaiseEvent(new AggregateItemAdded { MessageId = MessageExtensions.NewMessageId(), ProductId = productId, Quantity = quantity, Price = price });
        }

        public void Confirm()
        {
            RaiseEvent(new AggregateOrderConfirmed { MessageId = MessageExtensions.NewMessageId() });
        }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case AggregateOrderCreated e:
                    _id = e.OrderId;
                    CustomerId = e.CustomerId;
                    break;
                case AggregateItemAdded e:
                    TotalAmount += e.Quantity * e.Price;
                    ItemCount += e.Quantity;
                    break;
                case AggregateOrderConfirmed:
                    IsConfirmed = true;
                    break;
            }
        }
    }

    [MemoryPackable]
    private partial record AggregateOrderCreated : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; }
    }

    [MemoryPackable]
    private partial record AggregateItemAdded : IEvent
    {
        public required long MessageId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
        public required decimal Price { get; init; }
    }

    [MemoryPackable]
    private partial record AggregateOrderConfirmed : IEvent
    {
        public required long MessageId { get; init; }
    }

    #endregion
}
