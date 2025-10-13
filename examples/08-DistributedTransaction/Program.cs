using Catga;
using Catga.Messages;
using Catga.Transaction;
using Catga.InMemory.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================================
// Catga Distributed Transaction Example
// ============================================================================
// This demonstrates Catga's distributed transaction pattern, which is BETTER than traditional Saga:
//
// ✅ Event-driven (no central orchestrator needed)
// ✅ Automatic compensation on failure
// ✅ Built-in idempotency via Outbox/Inbox
// ✅ Event sourcing for full audit trail
// ✅ Declarative transaction definition
// ✅ Auto-retry with exponential backoff
// ✅ Timeout handling
//
// Use Case: E-Commerce Order Processing
// 1. Reserve Inventory
// 2. Charge Payment
// 3. Create Shipment
// ============================================================================

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // Register Catga
                services.AddCatga(options => options.EnableLogging = true)
                    .AddCatgaInMemoryTransport()
                    .AddCatgaInMemoryPersistence();

                // Register Transaction infrastructure
                services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
                services.AddSingleton<ITransactionCoordinator, TransactionCoordinator>();

                // Register handlers
                services.AddSingleton<IRequestHandler<ReserveInventoryCommand>, ReserveInventoryHandler>();
                services.AddSingleton<IRequestHandler<ReleaseInventoryCommand>, ReleaseInventoryHandler>();
                services.AddSingleton<IRequestHandler<ChargePaymentCommand>, ChargePaymentHandler>();
                services.AddSingleton<IRequestHandler<RefundPaymentCommand>, RefundPaymentHandler>();
                services.AddSingleton<IRequestHandler<CreateShipmentCommand>, CreateShipmentHandler>();
                services.AddSingleton<IRequestHandler<CancelShipmentCommand>, CancelShipmentHandler>();

                // Register transaction
                services.AddSingleton<OrderTransaction>();

                // Register business services
                services.AddSingleton<InventoryService>();
                services.AddSingleton<PaymentService>();
                services.AddSingleton<ShipmentService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .Build();

        await host.StartAsync();

        var coordinator = host.Services.GetRequiredService<ITransactionCoordinator>();
        var transaction = host.Services.GetRequiredService<OrderTransaction>();

        Console.WriteLine("=== Catga Distributed Transaction Example ===\n");

        // Example 1: Successful Order
        Console.WriteLine("--- Example 1: Successful Order ---");
        var successContext = new OrderContext
        {
            OrderId = "ORDER-001",
            CustomerId = "CUST-123",
            ProductId = "PROD-456",
            Quantity = 2,
            Amount = 99.99m
        };

        var result1 = await coordinator.StartAsync(transaction, successContext);
        Console.WriteLine($"Result: {result1.Status} - {(result1.IsSuccess ? "✅ Success" : $"❌ {result1.Error}")}\n");

        // Example 2: Failed Payment (Auto-Compensation)
        Console.WriteLine("--- Example 2: Failed Payment (Auto-Compensation) ---");
        var failedContext = new OrderContext
        {
            OrderId = "ORDER-002",
            CustomerId = "CUST-456",
            ProductId = "PROD-789",
            Quantity = 1,
            Amount = -50.00m // Invalid amount triggers failure
        };

        var result2 = await coordinator.StartAsync(transaction, failedContext);
        Console.WriteLine($"Result: {result2.Status} - {(result2.IsSuccess ? "✅ Success" : $"❌ {result2.Error}")}\n");

        Console.WriteLine("=== Transaction Examples Completed ===");
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();

        await host.StopAsync();
    }
}

// ============================================================================
// Transaction Context - Shared state across all steps
// ============================================================================
public class OrderContext
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal Amount { get; init; }

    // Populated during execution
    public string? ReservationId { get; set; }
    public string? PaymentId { get; set; }
    public string? ShipmentId { get; set; }
}

// ============================================================================
// Transaction Definition
// ============================================================================
public class OrderTransaction : IDistributedTransaction<OrderContext>
{
    public string TransactionId => "order-transaction";
    public string Name => "Order Processing Transaction";

    public ITransactionBuilder<OrderContext> Define(ITransactionBuilder<OrderContext> builder)
    {
        return builder
            // Step 1: Reserve Inventory
            .Execute<ReserveInventoryCommand, InventoryReservedEvent>(
                ctx => new ReserveInventoryCommand
                {
                    MessageId = Guid.NewGuid().ToString(),
                    ProductId = ctx.ProductId,
                    Quantity = ctx.Quantity
                },
                (ctx, evt) =>
                {
                    ctx.ReservationId = evt.ReservationId;
                    Console.WriteLine($"  ✅ Inventory reserved: {evt.ReservationId}");
                    return ctx;
                },
                (ctx, ex) =>
                {
                    Console.WriteLine($"  ❌ Inventory reservation failed: {ex.Message}");
                    return ctx;
                })
            .CompensateWith<ReleaseInventoryCommand>(ctx => new ReleaseInventoryCommand
            {
                MessageId = Guid.NewGuid().ToString(),
                ReservationId = ctx.ReservationId!
            })

            // Step 2: Charge Payment
            .Execute<ChargePaymentCommand, PaymentChargedEvent>(
                ctx => new ChargePaymentCommand
                {
                    MessageId = Guid.NewGuid().ToString(),
                    CustomerId = ctx.CustomerId,
                    Amount = ctx.Amount
                },
                (ctx, evt) =>
                {
                    ctx.PaymentId = evt.PaymentId;
                    Console.WriteLine($"  ✅ Payment charged: {evt.PaymentId}");
                    return ctx;
                })
            .CompensateWith<RefundPaymentCommand>(ctx => new RefundPaymentCommand
            {
                MessageId = Guid.NewGuid().ToString(),
                PaymentId = ctx.PaymentId!
            })

            // Step 3: Create Shipment
            .Execute<CreateShipmentCommand, ShipmentCreatedEvent>(
                ctx => new CreateShipmentCommand
                {
                    MessageId = Guid.NewGuid().ToString(),
                    OrderId = ctx.OrderId,
                    CustomerId = ctx.CustomerId
                },
                (ctx, evt) =>
                {
                    ctx.ShipmentId = evt.ShipmentId;
                    Console.WriteLine($"  ✅ Shipment created: {evt.ShipmentId}");
                    return ctx;
                })
            .CompensateWith<CancelShipmentCommand>(ctx => new CancelShipmentCommand
            {
                MessageId = Guid.NewGuid().ToString(),
                ShipmentId = ctx.ShipmentId!
            });
    }
}

// ============================================================================
// Commands and Events
// ============================================================================

// Inventory
public record ReserveInventoryCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string ProductId { get; init; }
    public int Quantity { get; init; }
}

public record ReleaseInventoryCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string ReservationId { get; init; }
}

public record InventoryReservedEvent : IEvent
{
    public required string MessageId { get; init; }
    public required string ReservationId { get; init; }
}

// Payment
public record ChargePaymentCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string CustomerId { get; init; }
    public decimal Amount { get; init; }
}

public record RefundPaymentCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string PaymentId { get; init; }
}

public record PaymentChargedEvent : IEvent
{
    public required string MessageId { get; init; }
    public required string PaymentId { get; init; }
}

// Shipment
public record CreateShipmentCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
}

public record CancelShipmentCommand : ICommand
{
    public required string MessageId { get; init; }
    public required string ShipmentId { get; init; }
}

public record ShipmentCreatedEvent : IEvent
{
    public required string MessageId { get; init; }
    public required string ShipmentId { get; init; }
}

// ============================================================================
// Command Handlers
// ============================================================================

public class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand>
{
    private readonly InventoryService _service;
    public ReserveInventoryHandler(InventoryService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(ReserveInventoryCommand request, CancellationToken cancellationToken = default)
    {
        var reservationId = await _service.ReserveAsync(request.ProductId, request.Quantity);
        return reservationId != null
            ? CatgaResult.Success()
            : CatgaResult.Failure("Insufficient inventory");
    }
}

public class ReleaseInventoryHandler : IRequestHandler<ReleaseInventoryCommand>
{
    private readonly InventoryService _service;
    public ReleaseInventoryHandler(InventoryService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(ReleaseInventoryCommand request, CancellationToken cancellationToken = default)
    {
        await _service.ReleaseAsync(request.ReservationId);
        Console.WriteLine($"  🔄 Inventory released: {request.ReservationId}");
        return CatgaResult.Success();
    }
}

public class ChargePaymentHandler : IRequestHandler<ChargePaymentCommand>
{
    private readonly PaymentService _service;
    public ChargePaymentHandler(PaymentService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(ChargePaymentCommand request, CancellationToken cancellationToken = default)
    {
        var paymentId = await _service.ChargeAsync(request.CustomerId, request.Amount);
        return paymentId != null
            ? CatgaResult.Success()
            : CatgaResult.Failure("Payment declined");
    }
}

public class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand>
{
    private readonly PaymentService _service;
    public RefundPaymentHandler(PaymentService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(RefundPaymentCommand request, CancellationToken cancellationToken = default)
    {
        await _service.RefundAsync(request.PaymentId);
        Console.WriteLine($"  🔄 Payment refunded: {request.PaymentId}");
        return CatgaResult.Success();
    }
}

public class CreateShipmentHandler : IRequestHandler<CreateShipmentCommand>
{
    private readonly ShipmentService _service;
    public CreateShipmentHandler(ShipmentService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(CreateShipmentCommand request, CancellationToken cancellationToken = default)
    {
        var shipmentId = await _service.CreateAsync(request.OrderId, request.CustomerId);
        return shipmentId != null
            ? CatgaResult.Success()
            : CatgaResult.Failure("Shipment creation failed");
    }
}

public class CancelShipmentHandler : IRequestHandler<CancelShipmentCommand>
{
    private readonly ShipmentService _service;
    public CancelShipmentHandler(ShipmentService service) => _service = service;

    public async Task<CatgaResult> HandleAsync(CancelShipmentCommand request, CancellationToken cancellationToken = default)
    {
        await _service.CancelAsync(request.ShipmentId);
        Console.WriteLine($"  🔄 Shipment cancelled: {request.ShipmentId}");
        return CatgaResult.Success();
    }
}

// ============================================================================
// Business Services (Simulated)
// ============================================================================

public class InventoryService
{
    private int _stock = 100;

    public Task<string?> ReserveAsync(string productId, int quantity)
    {
        if (quantity > _stock) return Task.FromResult<string?>(null);
        _stock -= quantity;
        return Task.FromResult<string?>($"RES-{Guid.NewGuid():N}");
    }

    public Task ReleaseAsync(string reservationId)
    {
        _stock += 1; // Simplified
        return Task.CompletedTask;
    }
}

public class PaymentService
{
    public Task<string?> ChargeAsync(string customerId, decimal amount)
    {
        if (amount <= 0) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>($"PAY-{Guid.NewGuid():N}");
    }

    public Task RefundAsync(string paymentId) => Task.CompletedTask;
}

public class ShipmentService
{
    public Task<string?> CreateAsync(string orderId, string customerId)
        => Task.FromResult<string?>($"SHIP-{Guid.NewGuid():N}");

    public Task CancelAsync(string shipmentId) => Task.CompletedTask;
}

