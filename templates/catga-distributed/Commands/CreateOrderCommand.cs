using Catga;

namespace CatgaDistributed.Commands;

/// <summary>
/// Command to create a new order
/// </summary>
public record CreateOrderCommand(
    long CustomerId,
    List<OrderItem> Items,
    string ShippingAddress
) : IRequest<CreateOrderResponse>;

public record OrderItem(
    long ProductId,
    int Quantity,
    decimal Price
);

public record CreateOrderResponse(
    long OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
);

/// <summary>
/// Handler for creating orders
/// </summary>
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;
#if (UseDistributedId)
    private readonly ISnowflakeIdGenerator _idGenerator;
#endif

    public CreateOrderHandler(
        ILogger<CreateOrderHandler> logger
#if (UseDistributedId)
        , ISnowflakeIdGenerator idGenerator
#endif
    )
    {
        _logger = logger;
#if (UseDistributedId)
        _idGenerator = idGenerator;
#endif
    }

    public async ValueTask<CreateOrderResponse> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating order for customer {CustomerId} with {ItemCount} items",
            request.CustomerId,
            request.Items.Count);

#if (UseDistributedId)
        var orderId = _idGenerator.NextId();
#else
        var orderId = Random.Shared.NextInt64(1, long.MaxValue);
#endif
        
        var totalAmount = request.Items.Sum(item => item.Price * item.Quantity);
        
        // TODO: Save order to database
        // TODO: Publish OrderCreated event
        
        await Task.CompletedTask;
        
        _logger.LogInformation(
            "Order {OrderId} created successfully with total amount {TotalAmount}",
            orderId,
            totalAmount);

        return new CreateOrderResponse(
            orderId,
            totalAmount,
            DateTime.UtcNow);
    }
}

