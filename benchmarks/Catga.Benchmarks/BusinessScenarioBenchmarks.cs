using BenchmarkDotNet.Attributes;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Benchmarks;

/// <summary>
/// Realistic business scenario benchmarks
/// Simulates common patterns: Order Processing, Payment, Notification
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class BusinessScenarioBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddCatga().UseMemoryPack();

        // Register handlers
        services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>, ProcessPaymentHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, GetOrderResult>, GetOrderQueryHandler>();
        services.AddScoped<IRequestHandler<GetUserOrdersQuery, GetUserOrdersResult>, GetUserOrdersQueryHandler>();

        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, SendEmailNotificationHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, UpdateInventoryHandler>();
        services.AddScoped<IEventHandler<PaymentProcessedEvent>, PaymentProcessedEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [Benchmark(Baseline = true, Description = "Create Order (Command)")]
    public async Task<CatgaResult<CreateOrderResult>> CreateOrder()
    {
        var command = new CreateOrderCommand(
            UserId: 123,
            ProductId: 456,
            Quantity: 2,
            TotalAmount: 99.99m
        );

        return await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command);
    }

    [Benchmark(Description = "Process Payment (Command)")]
    public async Task<CatgaResult<ProcessPaymentResult>> ProcessPayment()
    {
        var command = new ProcessPaymentCommand(
            OrderId: 789,
            Amount: 99.99m,
            PaymentMethod: "CreditCard"
        );

        return await _mediator.SendAsync<ProcessPaymentCommand, ProcessPaymentResult>(command);
    }

    [Benchmark(Description = "Get Order (Query)")]
    public async Task<CatgaResult<GetOrderResult>> GetOrder()
    {
        var query = new GetOrderQuery(OrderId: 789);
        return await _mediator.SendAsync<GetOrderQuery, GetOrderResult>(query);
    }

    [Benchmark(Description = "Get User Orders (Query with multiple results)")]
    public async Task<CatgaResult<GetUserOrdersResult>> GetUserOrders()
    {
        var query = new GetUserOrdersQuery(UserId: 123);
        return await _mediator.SendAsync<GetUserOrdersQuery, GetUserOrdersResult>(query);
    }

    [Benchmark(Description = "Order Created Event (3 handlers)")]
    public async Task PublishOrderCreatedEvent()
    {
        var @event = new OrderCreatedEvent(
            OrderId: 789,
            UserId: 123,
            ProductId: 456,
            Quantity: 2,
            TotalAmount: 99.99m
        );

        await _mediator.PublishAsync(@event);
    }

    [Benchmark(Description = "Complete Order Flow (Command + Event)")]
    public async Task CompleteOrderFlow()
    {
        // 1. Create Order
        var createCommand = new CreateOrderCommand(
            UserId: 123,
            ProductId: 456,
            Quantity: 2,
            TotalAmount: 99.99m
        );
        var createResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(createCommand);

        // 2. Publish Event
        if (createResult.IsSuccess && createResult.Value != null)
        {
            var orderEvent = new OrderCreatedEvent(
                OrderId: createResult.Value.OrderId,
                UserId: 123,
                ProductId: 456,
                Quantity: 2,
                TotalAmount: 99.99m
            );
            await _mediator.PublishAsync(orderEvent);
        }
    }

    [Benchmark(Description = "E-Commerce Scenario (Order + Payment + Query)")]
    public async Task ECommerceScenario()
    {
        // 1. Create Order
        var createOrder = new CreateOrderCommand(123, 456, 2, 99.99m);
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(createOrder);

        if (orderResult.IsSuccess && orderResult.Value != null)
        {
            // 2. Process Payment
            var payment = new ProcessPaymentCommand(orderResult.Value.OrderId, 99.99m, "CreditCard");
            var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, ProcessPaymentResult>(payment);

            if (paymentResult.IsSuccess && paymentResult.Value != null)
            {
                // 3. Query Order Status
                var query = new GetOrderQuery(orderResult.Value.OrderId);
                await _mediator.SendAsync<GetOrderQuery, GetOrderResult>(query);
            }
        }
    }

    [Benchmark(Description = "E-Commerce Scenario Batch (100 flows sequential)")]
    public async Task ECommerceScenarioBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            await RunECommerceFlow();
        }
    }

    [Benchmark(Description = "E-Commerce Scenario Concurrent (100 flows)")]
    public async Task ECommerceScenarioConcurrent()
    {
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = RunECommerceFlow();
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunECommerceFlow()
    {
        var createOrder = new CreateOrderCommand(123, 456, 2, 99.99m);
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(createOrder);
        if (orderResult.IsSuccess && orderResult.Value != null)
        {
            var payment = new ProcessPaymentCommand(orderResult.Value.OrderId, 99.99m, "CreditCard");
            var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, ProcessPaymentResult>(payment);
            if (paymentResult.IsSuccess && paymentResult.Value != null)
            {
                var query = new GetOrderQuery(orderResult.Value.OrderId);
                await _mediator.SendAsync<GetOrderQuery, GetOrderResult>(query);
            }
        }
    }

    [Benchmark(Description = "High-Throughput Batch (100 Orders)")]
    public async Task HighThroughputBatch()
    {
        var tasks = new Task<CatgaResult<CreateOrderResult>>[100];

        for (int i = 0; i < 100; i++)
        {
            var command = new CreateOrderCommand(
                UserId: 100 + i,
                ProductId: 200 + i,
                Quantity: 1,
                TotalAmount: 50.0m + i
            );
            tasks[i] = _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command).AsTask();
        }

        await Task.WhenAll(tasks);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

// ============================================================================
// Business Domain Messages
// ============================================================================

#region Commands

[MemoryPackable]
public partial record CreateOrderCommand(
    int UserId,
    int ProductId,
    int Quantity,
    decimal TotalAmount
) : IRequest<CreateOrderResult>;

[MemoryPackable]
public partial record CreateOrderResult(
    int OrderId,
    string Status,
    decimal TotalAmount
);

[MemoryPackable]
public partial record ProcessPaymentCommand(
    int OrderId,
    decimal Amount,
    string PaymentMethod
) : IRequest<ProcessPaymentResult>;

[MemoryPackable]
public partial record ProcessPaymentResult(
    int PaymentId,
    bool Success,
    string TransactionId
);

#endregion

#region Queries

[MemoryPackable]
public partial record GetOrderQuery(int OrderId) : IRequest<GetOrderResult>;

[MemoryPackable]
public partial record GetOrderResult(
    int OrderId,
    int UserId,
    string Status,
    decimal TotalAmount
);

[MemoryPackable]
public partial record GetUserOrdersQuery(int UserId) : IRequest<GetUserOrdersResult>;

[MemoryPackable]
public partial record GetUserOrdersResult(
    int UserId,
    int TotalOrders,
    decimal TotalSpent
);

#endregion

#region Events

[MemoryPackable]
public partial record OrderCreatedEvent(
    int OrderId,
    int UserId,
    int ProductId,
    int Quantity,
    decimal TotalAmount
) : IEvent;

[MemoryPackable]
public partial record PaymentProcessedEvent(
    int PaymentId,
    int OrderId,
    decimal Amount,
    string TransactionId
) : IEvent;

#endregion

// ============================================================================
// Business Handlers (Minimal Logic for Performance Testing)
// ============================================================================

#region Command Handlers

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    public Task<CatgaResult<CreateOrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // Simulate business logic: validate, create order
        var orderId = Random.Shared.Next(10000, 99999);
        var result = new CreateOrderResult(
            OrderId: orderId,
            Status: "Created",
            TotalAmount: request.TotalAmount
        );

        return Task.FromResult(CatgaResult<CreateOrderResult>.Success(result));
    }
}

public class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>
{
    public Task<CatgaResult<ProcessPaymentResult>> HandleAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        // Simulate payment processing
        var paymentId = Random.Shared.Next(20000, 29999);
        var transactionId = Guid.NewGuid().ToString("N")[..16];

        var result = new ProcessPaymentResult(
            PaymentId: paymentId,
            Success: true,
            TransactionId: transactionId
        );

        return Task.FromResult(CatgaResult<ProcessPaymentResult>.Success(result));
    }
}

#endregion

#region Query Handlers

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, GetOrderResult>
{
    public Task<CatgaResult<GetOrderResult>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        // Simulate database query
        var result = new GetOrderResult(
            OrderId: request.OrderId,
            UserId: 123,
            Status: "Completed",
            TotalAmount: 99.99m
        );

        return Task.FromResult(CatgaResult<GetOrderResult>.Success(result));
    }
}

public class GetUserOrdersQueryHandler : IRequestHandler<GetUserOrdersQuery, GetUserOrdersResult>
{
    public Task<CatgaResult<GetUserOrdersResult>> HandleAsync(
        GetUserOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        // Simulate aggregate query
        var result = new GetUserOrdersResult(
            UserId: request.UserId,
            TotalOrders: 15,
            TotalSpent: 1299.85m
        );

        return Task.FromResult(CatgaResult<GetUserOrdersResult>.Success(result));
    }
}

#endregion

#region Event Handlers

public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Simulate updating order status
        return Task.CompletedTask;
    }
}

public class SendEmailNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Simulate sending email
        return Task.CompletedTask;
    }
}

public class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Simulate updating inventory
        return Task.CompletedTask;
    }
}

public class PaymentProcessedEventHandler : IEventHandler<PaymentProcessedEvent>
{
    public Task HandleAsync(PaymentProcessedEvent @event, CancellationToken cancellationToken = default)
    {
        // Simulate updating payment records
        return Task.CompletedTask;
    }
}

#endregion

