using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for CQRS patterns.
/// Tests commands, queries, events, and mediator patterns.
/// </summary>
public class CqrsE2ETests
{
    [Fact]
    public async Task Command_SendRequest_ExecutesHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new CreateOrderCommand("CUST-001", 199.99m);

        // Act
        var result = await mediator.SendAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.OrderId.Should().NotBeNullOrEmpty();
        result.Status.Should().Be("Created");
    }

    [Fact]
    public async Task Query_SendRequest_ReturnsData()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<GetOrderQuery, OrderDto?>, GetOrderHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var query = new GetOrderQuery("ORD-001");

        // Act
        var result = await mediator.SendAsync(query);

        // Assert
        result.Should().NotBeNull();
        result!.OrderId.Should().Be("ORD-001");
    }

    [Fact]
    public async Task Event_Publish_NotifiesAllHandlers()
    {
        // Arrange
        var handlerCallCount = 0;
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IEventHandler<OrderCreatedEvent>>(new CountingEventHandler(() => Interlocked.Increment(ref handlerCallCount)));
        services.AddSingleton<IEventHandler<OrderCreatedEvent>>(new CountingEventHandler(() => Interlocked.Increment(ref handlerCallCount)));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var @event = new OrderCreatedEvent("ORD-001", "CUST-001", 299.99m);

        // Act
        await mediator.PublishAsync(@event);

        // Assert
        handlerCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Pipeline_WithBehavior_ExecutesBehavior()
    {
        // Arrange
        var behaviorExecuted = false;
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
        services.AddSingleton<IPipelineBehavior<CreateOrderCommand, OrderResult>>(
            new TestBehavior<CreateOrderCommand, OrderResult>(() => behaviorExecuted = true));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new CreateOrderCommand("CUST-001", 99.99m);

        // Act
        await mediator.SendAsync(command);

        // Assert
        behaviorExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task Batch_SendMultipleRequests_ProcessesAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var commands = new List<CreateOrderCommand>
        {
            new("CUST-001", 100m),
            new("CUST-002", 200m),
            new("CUST-003", 300m)
        };

        // Act
        var results = new List<OrderResult>();
        foreach (var command in commands)
        {
            var result = await mediator.SendAsync(command);
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Status.Should().Be("Created"));
    }

    [Fact]
    public async Task Validation_InvalidRequest_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
        services.AddSingleton<IPipelineBehavior<CreateOrderCommand, OrderResult>, ValidationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var invalidCommand = new CreateOrderCommand("", -100m); // Invalid: empty customer, negative amount

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await mediator.SendAsync(invalidCommand);
        });
    }

    [Fact]
    public async Task CommandWithNoResponse_Executes_Successfully()
    {
        // Arrange
        var executed = false;
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<DeleteOrderCommand>>(new DeleteOrderHandler(() => executed = true));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new DeleteOrderCommand("ORD-001");

        // Act
        await mediator.SendAsync(command);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task EventWithMultipleTypes_RoutesToCorrectHandler()
    {
        // Arrange
        var orderCreatedHandled = false;
        var orderShippedHandled = false;

        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IEventHandler<OrderCreatedEvent>>(new CountingEventHandler(() => orderCreatedHandled = true));
        services.AddSingleton<IEventHandler<OrderShippedEvent>>(new OrderShippedHandler(() => orderShippedHandled = true));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new OrderCreatedEvent("ORD-001", "CUST-001", 100m));
        await mediator.PublishAsync(new OrderShippedEvent("ORD-001", "TRK-12345"));

        // Assert
        orderCreatedHandled.Should().BeTrue();
        orderShippedHandled.Should().BeTrue();
    }

    #region Test Types

    public record CreateOrderCommand(string CustomerId, decimal Amount) : IRequest<OrderResult>;
    public record GetOrderQuery(string OrderId) : IRequest<OrderDto?>;
    public record DeleteOrderCommand(string OrderId) : IRequest;
    public record OrderResult(string OrderId, string Status);
    public record OrderDto(string OrderId, string CustomerId, decimal Amount);

    public record OrderCreatedEvent(string OrderId, string CustomerId, decimal Amount) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record OrderShippedEvent(string OrderId, string TrackingNumber) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
    {
        public ValueTask<OrderResult> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(new OrderResult($"ORD-{Guid.NewGuid():N}"[..12], "Created"));
        }
    }

    public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto?>
    {
        public ValueTask<OrderDto?> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
        {
            return ValueTask.FromResult<OrderDto?>(new OrderDto(request.OrderId, "CUST-001", 199.99m));
        }
    }

    public class DeleteOrderHandler : IRequestHandler<DeleteOrderCommand>
    {
        private readonly Action _onExecute;
        public DeleteOrderHandler(Action onExecute) => _onExecute = onExecute;

        public ValueTask HandleAsync(DeleteOrderCommand request, CancellationToken ct = default)
        {
            _onExecute();
            return ValueTask.CompletedTask;
        }
    }

    public class CountingEventHandler : IEventHandler<OrderCreatedEvent>
    {
        private readonly Action _onHandle;
        public CountingEventHandler(Action onHandle) => _onHandle = onHandle;

        public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
        {
            _onHandle();
            return ValueTask.CompletedTask;
        }
    }

    public class OrderShippedHandler : IEventHandler<OrderShippedEvent>
    {
        private readonly Action _onHandle;
        public OrderShippedHandler(Action onHandle) => _onHandle = onHandle;

        public ValueTask HandleAsync(OrderShippedEvent @event, CancellationToken ct = default)
        {
            _onHandle();
            return ValueTask.CompletedTask;
        }
    }

    public class TestBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly Action _onExecute;
        public TestBehavior(Action onExecute) => _onExecute = onExecute;

        public async ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct = default)
        {
            _onExecute();
            return await next();
        }
    }

    public class ValidationBehavior : IPipelineBehavior<CreateOrderCommand, OrderResult>
    {
        public async ValueTask<OrderResult> HandleAsync(CreateOrderCommand request, RequestHandlerDelegate<OrderResult> next, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId))
                throw new ValidationException("CustomerId is required");
            if (request.Amount <= 0)
                throw new ValidationException("Amount must be positive");

            return await next();
        }
    }

    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }

    #endregion
}
