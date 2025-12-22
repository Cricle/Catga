using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Complete E2E tests covering InMemory, Redis, and NATS backends
/// Tests full CQRS + Event Sourcing workflow with all backends
/// Each test creates its own isolated containers to avoid test interference
/// </summary>
[Trait("Category", "E2E")]
[Trait("Requires", "Docker")]
[Collection("E2E Tests")]  // Force sequential execution
public class CompleteBackendE2ETests
{

    private static bool IsDockerRunning()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clear CatgaMediator's static caches to avoid test interference.
    /// This is necessary because the static caches persist across tests.
    /// </summary>
    private static void ClearMediatorCaches()
    {
        var mediatorType = typeof(CatgaMediator);
        var handlerCacheField = mediatorType.GetField("_handlerCache", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var behaviorCacheField = mediatorType.GetField("_behaviorCache", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (handlerCacheField?.GetValue(null) is System.Collections.IDictionary handlerCache)
            handlerCache.Clear();
        
        if (behaviorCacheField?.GetValue(null) is System.Collections.IDictionary behaviorCache)
            behaviorCache.Clear();
    }

    #region Test 1: Complete Order Flow - InMemory

    [Fact]
    public async Task CompleteOrderFlow_InMemory_ShouldWorkEndToEnd()
    {
        // Clear static caches to avoid test interference
        ClearMediatorCaches();
        
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMemoryPack()
            .UseInMemory();
        services.AddInMemoryTransport();
        
        // Register handlers manually - use Transient to avoid static caching issues
        services.AddTransient<IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>, E2ECreateOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EPayOrderCommand>, E2EPayOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EShipOrderCommand>, E2EShipOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EGetOrderQuery, E2EOrderDto?>, E2EGetOrderQueryHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var eventStore = provider.GetRequiredService<IEventStore>();

        var orderId = Guid.NewGuid().ToString("N");

        // Act & Assert - Create Order
        var createResult = await mediator.SendAsync<E2ECreateOrderCommand, E2EOrderCreatedResult>(
            new E2ECreateOrderCommand { OrderId = orderId, CustomerId = "customer-1", Amount = 100m });
        
        createResult.IsSuccess.Should().BeTrue();
        createResult.Value!.OrderId.Should().Be(orderId);

        // Act & Assert - Pay Order
        var payResult = await mediator.SendAsync<E2EPayOrderCommand>(
            new E2EPayOrderCommand { OrderId = orderId });
        
        payResult.IsSuccess.Should().BeTrue();

        // Act & Assert - Ship Order
        var shipResult = await mediator.SendAsync<E2EShipOrderCommand>(
            new E2EShipOrderCommand { OrderId = orderId, TrackingNumber = "TRACK-123" });
        
        shipResult.IsSuccess.Should().BeTrue();

        // Act & Assert - Verify Events
        var events = await eventStore.ReadAsync(orderId);
        events.Events.Should().HaveCount(3);
        events.Events[0].Event.Should().BeOfType<E2EOrderCreatedEvent>();
        events.Events[1].Event.Should().BeOfType<E2EOrderPaidEvent>();
        events.Events[2].Event.Should().BeOfType<E2EOrderShippedEvent>();

        // Act & Assert - Query Order
        var queryResult = await mediator.SendAsync<E2EGetOrderQuery, E2EOrderDto?>(
            new E2EGetOrderQuery { OrderId = orderId });
        
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value.Should().NotBeNull();
        queryResult.Value!.OrderId.Should().Be(orderId);
        queryResult.Value.Status.Should().Be("Shipped");
    }

    #endregion

    #region Test 2: Complete Order Flow - Redis

    [Fact]
    public async Task CompleteOrderFlow_Redis_ShouldWorkEndToEnd()
    {
        if (!IsDockerRunning()) return;

        // Clear static caches to avoid test interference
        ClearMediatorCaches();

        // Arrange - Create isolated Redis container for this test
        await using var redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await redisContainer.StartAsync();
        var redisConnectionString = redisContainer.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        
        // Register Catga first
        services.AddCatga()
            .UseMemoryPack()
            .UseRedis(redisConnectionString);
        
        services.AddRedisTransport(redisConnectionString);
        
        // Register handlers manually - use Transient to avoid static caching issues
        services.AddTransient<IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>, E2ECreateOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EPayOrderCommand>, E2EPayOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EShipOrderCommand>, E2EShipOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EGetOrderQuery, E2EOrderDto?>, E2EGetOrderQueryHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var eventStore = provider.GetRequiredService<IEventStore>();

        var orderId = Guid.NewGuid().ToString("N");

        // Act & Assert - Create Order
        var createResult = await mediator.SendAsync<E2ECreateOrderCommand, E2EOrderCreatedResult>(
            new E2ECreateOrderCommand { OrderId = orderId, CustomerId = "customer-redis", Amount = 200m });
        
        createResult.IsSuccess.Should().BeTrue();
        createResult.Value!.OrderId.Should().Be(orderId);

        // Act & Assert - Pay Order
        var payResult = await mediator.SendAsync<E2EPayOrderCommand>(
            new E2EPayOrderCommand { OrderId = orderId });
        
        payResult.IsSuccess.Should().BeTrue();

        // Act & Assert - Ship Order
        var shipResult = await mediator.SendAsync<E2EShipOrderCommand>(
            new E2EShipOrderCommand { OrderId = orderId, TrackingNumber = "TRACK-REDIS" });
        
        shipResult.IsSuccess.Should().BeTrue();

        // Wait a bit for Redis to persist all events
        await Task.Delay(500);

        // Act & Assert - Verify Events
        var events = await eventStore.ReadAsync(orderId);
        events.Events.Should().HaveCount(3);
        events.Events[0].Event.Should().BeOfType<E2EOrderCreatedEvent>();
        events.Events[1].Event.Should().BeOfType<E2EOrderPaidEvent>();
        events.Events[2].Event.Should().BeOfType<E2EOrderShippedEvent>();

        // Act & Assert - Query Order
        var queryResult = await mediator.SendAsync<E2EGetOrderQuery, E2EOrderDto?>(
            new E2EGetOrderQuery { OrderId = orderId });
        
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value.Should().NotBeNull();
        queryResult.Value!.OrderId.Should().Be(orderId);
        queryResult.Value.Status.Should().Be("Shipped");
    }

    #endregion

    #region Test 3: Complete Order Flow - NATS

    [Fact]
    public async Task CompleteOrderFlow_NATS_ShouldWorkEndToEnd()
    {
        if (!IsDockerRunning()) return;

        // Clear static caches to avoid test interference
        ClearMediatorCaches();

        // Arrange - Create isolated NATS container for this test
        await using var natsContainer = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();
        await natsContainer.StartAsync();
        await Task.Delay(5000); // Wait longer for NATS to be fully ready
        var natsPort = natsContainer.GetMappedPublicPort(4222);
        var natsUrl = $"nats://localhost:{natsPort}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNatsConnection(natsUrl);
        
        // Register Catga first
        services.AddCatga()
            .UseMemoryPack()
            .UseNats();
        
        services.AddNatsTransport(natsUrl);
        
        // Register handlers manually - use Transient to avoid static caching issues
        services.AddTransient<IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>, E2ECreateOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EPayOrderCommand>, E2EPayOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EShipOrderCommand>, E2EShipOrderCommandHandler>();
        services.AddTransient<IRequestHandler<E2EGetOrderQuery, E2EOrderDto?>, E2EGetOrderQueryHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var eventStore = provider.GetRequiredService<IEventStore>();

        var orderId = Guid.NewGuid().ToString("N");

        // Act & Assert - Create Order
        var createResult = await mediator.SendAsync<E2ECreateOrderCommand, E2EOrderCreatedResult>(
            new E2ECreateOrderCommand { OrderId = orderId, CustomerId = "customer-nats", Amount = 300m });
        
        createResult.IsSuccess.Should().BeTrue($"Create order should succeed, but got error: {createResult.Error}");
        createResult.Value!.OrderId.Should().Be(orderId);

        // Act & Assert - Pay Order
        var payResult = await mediator.SendAsync<E2EPayOrderCommand>(
            new E2EPayOrderCommand { OrderId = orderId });
        
        payResult.IsSuccess.Should().BeTrue($"Pay order should succeed, but got error: {payResult.Error}");

        // Act & Assert - Ship Order
        var shipResult = await mediator.SendAsync<E2EShipOrderCommand>(
            new E2EShipOrderCommand { OrderId = orderId, TrackingNumber = "TRACK-NATS" });
        
        shipResult.IsSuccess.Should().BeTrue($"Ship order should succeed, but got error: {shipResult.Error}");

        // Wait a bit for NATS to persist all events
        await Task.Delay(2000);

        // Act & Assert - Verify Events
        var events = await eventStore.ReadAsync(orderId);
        events.Events.Should().HaveCount(3);
        events.Events[0].Event.Should().BeOfType<E2EOrderCreatedEvent>();
        events.Events[1].Event.Should().BeOfType<E2EOrderPaidEvent>();
        events.Events[2].Event.Should().BeOfType<E2EOrderShippedEvent>();

        // Act & Assert - Query Order
        var queryResult = await mediator.SendAsync<E2EGetOrderQuery, E2EOrderDto?>(
            new E2EGetOrderQuery { OrderId = orderId });
        
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value.Should().NotBeNull();
        queryResult.Value!.OrderId.Should().Be(orderId);
        queryResult.Value.Status.Should().Be("Shipped");
    }

    #endregion

    #region Test 4: Snapshot Store - All Backends

    [Fact]
    public async Task SnapshotStore_InMemory_ShouldPersistAndRestore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMemoryPack()
            .UseInMemory();
        
        var provider = services.BuildServiceProvider();
        var snapshotStore = provider.GetRequiredService<ISnapshotStore>();

        var aggregateId = Guid.NewGuid().ToString("N");
        var snapshot = new E2EOrderSnapshot
        {
            AggregateId = aggregateId,
            Version = 5,
            Status = "Paid",
            TotalAmount = 150m,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await snapshotStore.SaveAsync(aggregateId, snapshot, 5);
        var restored = await snapshotStore.LoadAsync<E2EOrderSnapshot>(aggregateId);

        // Assert
        restored.Should().NotBeNull();
        restored!.Value.State.AggregateId.Should().Be(aggregateId);
        restored.Value.Version.Should().Be(5);
        restored.Value.State.Status.Should().Be("Paid");
        restored.Value.State.TotalAmount.Should().Be(150m);
    }

    [Fact]
    public async Task SnapshotStore_Redis_ShouldPersistAndRestore()
    {
        if (!IsDockerRunning()) return;

        // Arrange - Create isolated Redis container for this test
        await using var redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await redisContainer.StartAsync();
        var redisConnectionString = redisContainer.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMemoryPack()
            .UseRedis(redisConnectionString);
        
        var provider = services.BuildServiceProvider();
        var snapshotStore = provider.GetRequiredService<ISnapshotStore>();

        var aggregateId = Guid.NewGuid().ToString("N");
        var snapshot = new E2EOrderSnapshot
        {
            AggregateId = aggregateId,
            Version = 10,
            Status = "Shipped",
            TotalAmount = 250m,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await snapshotStore.SaveAsync(aggregateId, snapshot, 10);
        var restored = await snapshotStore.LoadAsync<E2EOrderSnapshot>(aggregateId);

        // Assert
        restored.Should().NotBeNull();
        restored!.Value.State.AggregateId.Should().Be(aggregateId);
        restored.Value.Version.Should().Be(10);
        restored.Value.State.Status.Should().Be("Shipped");
        restored.Value.State.TotalAmount.Should().Be(250m);
    }

    [Fact]
    public async Task SnapshotStore_NATS_ShouldPersistAndRestore()
    {
        if (!IsDockerRunning()) return;

        // Arrange - Create isolated NATS container for this test
        await using var natsContainer = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();
        await natsContainer.StartAsync();
        await Task.Delay(2000); // Wait for NATS to be fully ready
        var natsPort = natsContainer.GetMappedPublicPort(4222);
        var natsUrl = $"nats://localhost:{natsPort}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNatsConnection(natsUrl);
        services.AddCatga()
            .UseMemoryPack()
            .UseNats();
        
        var provider = services.BuildServiceProvider();
        var snapshotStore = provider.GetRequiredService<ISnapshotStore>();

        var aggregateId = Guid.NewGuid().ToString("N");
        var snapshot = new E2EOrderSnapshot
        {
            AggregateId = aggregateId,
            Version = 15,
            Status = "Delivered",
            TotalAmount = 350m,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await snapshotStore.SaveAsync(aggregateId, snapshot, 15);
        var restored = await snapshotStore.LoadAsync<E2EOrderSnapshot>(aggregateId);

        // Assert
        restored.Should().NotBeNull();
        restored!.Value.State.AggregateId.Should().Be(aggregateId);
        restored.Value.Version.Should().Be(15);
        restored.Value.State.Status.Should().Be("Delivered");
        restored.Value.State.TotalAmount.Should().Be(350m);
    }

    #endregion

    #region Test 5: Event Publishing - All Backends

    [Fact]
    public async Task EventPublishing_InMemory_ShouldDeliverToHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMemoryPack()
            .UseInMemory();
        services.AddInMemoryTransport();
        services.AddScoped<IEventHandler<E2EOrderCreatedEvent>, E2EOrderCreatedEventHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        E2EOrderCreatedEventHandler.ReceivedEvents.Clear();

        // Act
        await mediator.PublishAsync(new E2EOrderCreatedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = "test-order",
            CustomerId = "test-customer",
            Amount = 100m
        });

        await Task.Delay(500); // Wait for async processing

        // Assert
        E2EOrderCreatedEventHandler.ReceivedEvents.Should().HaveCount(1);
        E2EOrderCreatedEventHandler.ReceivedEvents[0].OrderId.Should().Be("test-order");
    }

    [Fact]
    public async Task EventPublishing_Redis_ShouldDeliverToHandlers()
    {
        if (!IsDockerRunning()) return;

        // Arrange - Create isolated Redis container for this test
        await using var redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await redisContainer.StartAsync();
        var redisConnectionString = redisContainer.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMemoryPack()
            .UseRedis(redisConnectionString);
        services.AddRedisTransport(redisConnectionString);
        services.AddScoped<IEventHandler<E2EOrderCreatedEvent>, E2EOrderCreatedEventHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        E2EOrderCreatedEventHandler.ReceivedEvents.Clear();

        // Act
        await mediator.PublishAsync(new E2EOrderCreatedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = "redis-order",
            CustomerId = "redis-customer",
            Amount = 200m
        });

        await Task.Delay(1000); // Wait for async processing

        // Assert
        E2EOrderCreatedEventHandler.ReceivedEvents.Should().HaveCount(1);
        E2EOrderCreatedEventHandler.ReceivedEvents[0].OrderId.Should().Be("redis-order");
    }

    [Fact]
    public async Task EventPublishing_NATS_ShouldDeliverToHandlers()
    {
        if (!IsDockerRunning()) return;

        // Arrange - Create isolated NATS container for this test
        await using var natsContainer = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();
        await natsContainer.StartAsync();
        await Task.Delay(2000); // Wait for NATS to be fully ready
        var natsPort = natsContainer.GetMappedPublicPort(4222);
        var natsUrl = $"nats://localhost:{natsPort}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNatsConnection(natsUrl);
        services.AddCatga()
            .UseMemoryPack()
            .UseNats();
        services.AddNatsTransport(natsUrl);
        services.AddScoped<IEventHandler<E2EOrderCreatedEvent>, E2EOrderCreatedEventHandler>();
        
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        E2EOrderCreatedEventHandler.ReceivedEvents.Clear();

        // Act
        await mediator.PublishAsync(new E2EOrderCreatedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = "nats-order",
            CustomerId = "nats-customer",
            Amount = 300m
        });

        await Task.Delay(1000); // Wait for async processing

        // Assert
        E2EOrderCreatedEventHandler.ReceivedEvents.Should().HaveCount(1);
        E2EOrderCreatedEventHandler.ReceivedEvents[0].OrderId.Should().Be("nats-order");
    }

    #endregion
}

#region Test Models and Handlers for CompleteBackendE2ETests

[MemoryPackable]
public partial class E2ECreateOrderCommand : IRequest<E2EOrderCreatedResult>
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial class E2EPayOrderCommand : IRequest
{
    public required string OrderId { get; init; }
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial class E2EShipOrderCommand : IRequest
{
    public required string OrderId { get; init; }
    public required string TrackingNumber { get; init; }
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial class E2EGetOrderQuery : IRequest<E2EOrderDto?>
{
    public required string OrderId { get; init; }
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial class E2EOrderCreatedResult
{
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
}

[MemoryPackable]
public partial class E2EOrderDto
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
}

[MemoryPackable]
public partial class E2EOrderCreatedEvent : IEvent
{
    public required long MessageId { get; init; }
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

[MemoryPackable]
public partial class E2EOrderPaidEvent : IEvent
{
    public required long MessageId { get; init; }
    public required string OrderId { get; init; }
}

[MemoryPackable]
public partial class E2EOrderShippedEvent : IEvent
{
    public required long MessageId { get; init; }
    public required string OrderId { get; init; }
    public required string TrackingNumber { get; init; }
}

[MemoryPackable]
public partial class E2EOrderSnapshot
{
    public required string AggregateId { get; init; }
    public required long Version { get; init; }
    public required string Status { get; init; }
    public required decimal TotalAmount { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public class E2ECreateOrderCommandHandler : IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>
{
    private readonly IEventStore _eventStore;
    private readonly ICatgaMediator _mediator;

    public E2ECreateOrderCommandHandler(IEventStore eventStore, ICatgaMediator mediator)
    {
        _eventStore = eventStore;
        _mediator = mediator;
    }

    public async ValueTask<CatgaResult<E2EOrderCreatedResult>> HandleAsync(
        E2ECreateOrderCommand request, CancellationToken cancellationToken = default)
    {
        var @event = new E2EOrderCreatedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount
        };

        await _eventStore.AppendAsync(request.OrderId, new[] { @event }, expectedVersion: -1, cancellationToken);
        await _mediator.PublishAsync(@event, cancellationToken);

        return CatgaResult<E2EOrderCreatedResult>.Success(
            new E2EOrderCreatedResult { OrderId = request.OrderId, Amount = request.Amount });
    }
}

public class E2EPayOrderCommandHandler : IRequestHandler<E2EPayOrderCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ICatgaMediator _mediator;

    public E2EPayOrderCommandHandler(IEventStore eventStore, ICatgaMediator mediator)
    {
        _eventStore = eventStore;
        _mediator = mediator;
    }

    public async ValueTask<CatgaResult> HandleAsync(
        E2EPayOrderCommand request, CancellationToken cancellationToken = default)
    {
        var @event = new E2EOrderPaidEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = request.OrderId
        };

        await _eventStore.AppendAsync(request.OrderId, new[] { @event }, expectedVersion: -1, cancellationToken);
        await _mediator.PublishAsync(@event, cancellationToken);

        return CatgaResult.Success();
    }
}

public class E2EShipOrderCommandHandler : IRequestHandler<E2EShipOrderCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ICatgaMediator _mediator;

    public E2EShipOrderCommandHandler(IEventStore eventStore, ICatgaMediator mediator)
    {
        _eventStore = eventStore;
        _mediator = mediator;
    }

    public async ValueTask<CatgaResult> HandleAsync(
        E2EShipOrderCommand request, CancellationToken cancellationToken = default)
    {
        var @event = new E2EOrderShippedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = request.OrderId,
            TrackingNumber = request.TrackingNumber
        };

        await _eventStore.AppendAsync(request.OrderId, new[] { @event }, expectedVersion: -1, cancellationToken);
        await _mediator.PublishAsync(@event, cancellationToken);

        return CatgaResult.Success();
    }
}

public class E2EGetOrderQueryHandler : IRequestHandler<E2EGetOrderQuery, E2EOrderDto?>
{
    private readonly IEventStore _eventStore;

    public E2EGetOrderQueryHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async ValueTask<CatgaResult<E2EOrderDto?>> HandleAsync(
        E2EGetOrderQuery request, CancellationToken cancellationToken = default)
    {
        var result = await _eventStore.ReadAsync(request.OrderId, fromVersion: 0, int.MaxValue, cancellationToken);
        
        if (result.Events.Count == 0)
            return CatgaResult<E2EOrderDto?>.Success(null);

        var createdEvent = result.Events.Select(e => e.Event).OfType<E2EOrderCreatedEvent>().FirstOrDefault();
        if (createdEvent == null)
            return CatgaResult<E2EOrderDto?>.Success(null);

        var events = result.Events.Select(e => e.Event).ToList();
        var status = events.OfType<E2EOrderShippedEvent>().Any() ? "Shipped" :
                    events.OfType<E2EOrderPaidEvent>().Any() ? "Paid" : "Created";

        var dto = new E2EOrderDto
        {
            OrderId = request.OrderId,
            CustomerId = createdEvent.CustomerId,
            Status = status,
            Amount = createdEvent.Amount
        };

        return CatgaResult<E2EOrderDto?>.Success(dto);
    }
}

public class E2EOrderCreatedEventHandler : IEventHandler<E2EOrderCreatedEvent>
{
    public static readonly List<E2EOrderCreatedEvent> ReceivedEvents = new();

    public ValueTask HandleAsync(E2EOrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        ReceivedEvents.Add(@event);
        return ValueTask.CompletedTask;
    }
}

#endregion
