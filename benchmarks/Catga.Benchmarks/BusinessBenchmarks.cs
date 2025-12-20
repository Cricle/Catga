using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Resilience;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Benchmarks;

/// <summary>
/// Business scenario benchmarks - simulates real-world e-commerce operations.
/// Run: dotnet run -c Release -- --filter *Business*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BusinessBenchmarks
{
    private ICatgaMediator _mediator = null!;
    private IServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCatga().UseMemoryPack();
        services.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();

        // Commands
        services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentCommand, PaymentResult>, ProcessPaymentHandler>();

        // Queries
        services.AddScoped<IRequestHandler<GetOrderQuery, OrderResult>, GetOrderQueryHandler>();

        // Events (3 handlers for OrderCreated)
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, SendNotificationHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, UpdateInventoryHandler>();

        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<ICatgaMediator>();
    }

    [GlobalCleanup]
    public void Cleanup() => (_provider as IDisposable)?.Dispose();

    [Benchmark(Baseline = true, Description = "Create Order")]
    public ValueTask<CatgaResult<OrderResult>> CreateOrder()
        => _mediator.SendAsync<CreateOrderCommand, OrderResult>(
            new CreateOrderCommand(123, "PROD-001", 2, 99.99m));

    [Benchmark(Description = "Process Payment")]
    public ValueTask<CatgaResult<PaymentResult>> ProcessPayment()
        => _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(
            new ProcessPaymentCommand(789, 99.99m, "Alipay"));

    [Benchmark(Description = "Get Order")]
    public ValueTask<CatgaResult<OrderResult>> GetOrder()
        => _mediator.SendAsync<GetOrderQuery, OrderResult>(new GetOrderQuery(789));

    [Benchmark(Description = "Order Event (3 handlers)")]
    public Task PublishOrderEvent()
        => _mediator.PublishAsync(new OrderCreatedEvent(789, 123, "PROD-001", 2, 99.99m));

    [Benchmark(Description = "Full Order Flow")]
    public async Task FullOrderFlow()
    {
        // 1. Create order
        var order = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(
            new CreateOrderCommand(123, "PROD-001", 2, 99.99m));

        if (order.IsSuccess)
        {
            // 2. Publish event
            await _mediator.PublishAsync(
                new OrderCreatedEvent(order.Value!.OrderId, 123, "PROD-001", 2, 99.99m));
        }
    }

    [Benchmark(Description = "E-Commerce Flow (Order+Payment+Query)")]
    public async Task ECommerceFlow()
    {
        var order = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(
            new CreateOrderCommand(123, "PROD-001", 2, 99.99m));

        if (order.IsSuccess)
        {
            var payment = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(
                new ProcessPaymentCommand(order.Value!.OrderId, 99.99m, "Alipay"));

            if (payment.IsSuccess)
            {
                await _mediator.SendAsync<GetOrderQuery, OrderResult>(
                    new GetOrderQuery(order.Value.OrderId));
            }
        }
    }

    [Benchmark(Description = "Batch 10 Orders")]
    public async Task BatchOrders10()
    {
        var tasks = new ValueTask<CatgaResult<OrderResult>>[10];
        for (int i = 0; i < 10; i++)
            tasks[i] = _mediator.SendAsync<CreateOrderCommand, OrderResult>(
                new CreateOrderCommand(100 + i, $"PROD-{i:D3}", 1, 50m + i));
        for (int i = 0; i < 10; i++)
            await tasks[i];
    }

    [Benchmark(Description = "Concurrent 10 Orders")]
    public async Task ConcurrentOrders10()
    {
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = _mediator.SendAsync<CreateOrderCommand, OrderResult>(
                new CreateOrderCommand(100 + idx, $"PROD-{idx:D3}", 1, 50m + idx)).AsTask();
        }
        await Task.WhenAll(tasks);
    }
}

#region Commands

[MemoryPackable]
public partial record CreateOrderCommand(int UserId, string ProductId, int Quantity, decimal Amount) : IRequest<OrderResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record ProcessPaymentCommand(int OrderId, decimal Amount, string Method) : IRequest<PaymentResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record GetOrderQuery(int OrderId) : IRequest<OrderResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

#endregion

#region Results

[MemoryPackable]
public partial record OrderResult(int OrderId, string Status, decimal Amount);

[MemoryPackable]
public partial record PaymentResult(int PaymentId, bool Success, string TransactionId);

#endregion

#region Events

[MemoryPackable]
public partial record OrderCreatedEvent(int OrderId, int UserId, string ProductId, int Quantity, decimal Amount) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

#endregion

#region Handlers

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
    {
        var orderId = Random.Shared.Next(10000, 99999);
        return new(CatgaResult<OrderResult>.Success(new OrderResult(orderId, "Created", request.Amount)));
    }
}

public sealed class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, PaymentResult>
{
    public ValueTask<CatgaResult<PaymentResult>> HandleAsync(ProcessPaymentCommand request, CancellationToken ct = default)
    {
        var paymentId = Random.Shared.Next(20000, 29999);
        var txId = Guid.NewGuid().ToString("N")[..16];
        return new(CatgaResult<PaymentResult>.Success(new PaymentResult(paymentId, true, txId)));
    }
}

public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
        => new(CatgaResult<OrderResult>.Success(new OrderResult(request.OrderId, "Completed", 99.99m)));
}

public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

public sealed class SendNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

public sealed class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

#endregion
