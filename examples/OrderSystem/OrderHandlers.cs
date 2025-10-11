using Catga;
using Catga.Handlers;
using Catga.Results;
using Microsoft.EntityFrameworkCore;

namespace OrderSystem;

// ==================== Command Handlers ====================

/// <summary>
/// Create order command handler
/// </summary>
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly OrderDbContext _dbContext;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        OrderDbContext dbContext,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _dbContext = dbContext;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<CreateOrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // Generate order number
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

        // Calculate total amount
        var totalAmount = request.Items.Sum(item => item.Price * item.Quantity);

        // Create order
        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerName = request.CustomerName,
            TotalAmount = totalAmount,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Create order items
        foreach (var item in request.Items)
        {
            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                Price = item.Price
            };
            _dbContext.OrderItems.Add(orderItem);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order created: {OrderNumber} for {CustomerName}", orderNumber, request.CustomerName);

        // Publish event
        await _mediator.PublishAsync(new OrderCreatedEvent(order.Id, order.OrderNumber, order.CustomerName, order.TotalAmount), cancellationToken: cancellationToken);

        return CatgaResult<CreateOrderResult>.Success(new CreateOrderResult(order.Id, order.OrderNumber));
    }
}

/// <summary>
/// Process order command handler
/// </summary>
public class ProcessOrderHandler : IRequestHandler<ProcessOrderCommand, bool>
{
    private readonly OrderDbContext _dbContext;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ProcessOrderHandler> _logger;

    public ProcessOrderHandler(
        OrderDbContext dbContext,
        ICatgaMediator mediator,
        ILogger<ProcessOrderHandler> logger)
    {
        _dbContext = dbContext;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<bool>> HandleAsync(
        ProcessOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order == null)
            return CatgaResult<bool>.Failure("Order not found");

        if (order.Status != OrderStatus.Pending)
            return CatgaResult<bool>.Failure($"Order is already {order.Status}");

        order.Status = OrderStatus.Processing;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order processing: {OrderNumber}", order.OrderNumber);

        // Publish event
        await _mediator.PublishAsync(new OrderProcessingEvent(order.Id, order.OrderNumber), cancellationToken: cancellationToken);

        return CatgaResult<bool>.Success(true);
    }
}

/// <summary>
/// Complete order command handler
/// </summary>
public class CompleteOrderHandler : IRequestHandler<CompleteOrderCommand, bool>
{
    private readonly OrderDbContext _dbContext;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CompleteOrderHandler> _logger;

    public CompleteOrderHandler(
        OrderDbContext dbContext,
        ICatgaMediator mediator,
        ILogger<CompleteOrderHandler> logger)
    {
        _dbContext = dbContext;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<bool>> HandleAsync(
        CompleteOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order == null)
            return CatgaResult<bool>.Failure("Order not found");

        if (order.Status != OrderStatus.Processing)
            return CatgaResult<bool>.Failure($"Order must be in Processing status, current: {order.Status}");

        order.Status = OrderStatus.Completed;
        order.CompletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order completed: {OrderNumber}", order.OrderNumber);

        // Publish event
        await _mediator.PublishAsync(new OrderCompletedEvent(order.Id, order.OrderNumber, order.CompletedAt.Value), cancellationToken: cancellationToken);

        return CatgaResult<bool>.Success(true);
    }
}

/// <summary>
/// Cancel order command handler
/// </summary>
public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, bool>
{
    private readonly OrderDbContext _dbContext;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(
        OrderDbContext dbContext,
        ICatgaMediator mediator,
        ILogger<CancelOrderHandler> logger)
    {
        _dbContext = dbContext;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<bool>> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order == null)
            return CatgaResult<bool>.Failure("Order not found");

        if (order.Status == OrderStatus.Completed)
            return CatgaResult<bool>.Failure("Cannot cancel completed order");

        if (order.Status == OrderStatus.Cancelled)
            return CatgaResult<bool>.Failure("Order is already cancelled");

        order.Status = OrderStatus.Cancelled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order cancelled: {OrderNumber}, Reason: {Reason}", order.OrderNumber, request.Reason);

        // Publish event
        await _mediator.PublishAsync(new OrderCancelledEvent(order.Id, order.OrderNumber, request.Reason), cancellationToken: cancellationToken);

        return CatgaResult<bool>.Success(true);
    }
}

// ==================== Query Handlers ====================

/// <summary>
/// Get order query handler
/// </summary>
public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto?>
{
    private readonly OrderDbContext _dbContext;

    public GetOrderHandler(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatgaResult<OrderDto?>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
            return CatgaResult<OrderDto?>.Success(null);

        var orderDto = new OrderDto(order.Id, order.OrderNumber, order.CustomerName, order.TotalAmount, order.Status.ToString(), order.CreatedAt, order.CompletedAt);

        return CatgaResult<OrderDto?>.Success(orderDto);
    }
}

/// <summary>
/// Get orders by customer query handler
/// </summary>
public class GetOrdersByCustomerHandler : IRequestHandler<GetOrdersByCustomerQuery, List<OrderDto>>
{
    private readonly OrderDbContext _dbContext;

    public GetOrdersByCustomerHandler(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatgaResult<List<OrderDto>>> HandleAsync(
        GetOrdersByCustomerQuery request,
        CancellationToken cancellationToken = default)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CustomerName == request.CustomerName)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto(o.Id, o.OrderNumber, o.CustomerName, o.TotalAmount, o.Status.ToString(), o.CreatedAt, o.CompletedAt))
            .ToListAsync(cancellationToken);

        return CatgaResult<List<OrderDto>>.Success(orders);
    }
}

/// <summary>
/// Get pending orders query handler
/// </summary>
public class GetPendingOrdersHandler : IRequestHandler<GetPendingOrdersQuery, List<OrderDto>>
{
    private readonly OrderDbContext _dbContext;

    public GetPendingOrdersHandler(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatgaResult<List<OrderDto>>> HandleAsync(
        GetPendingOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new OrderDto(o.Id, o.OrderNumber, o.CustomerName, o.TotalAmount, o.Status.ToString(), o.CreatedAt, o.CompletedAt))
            .ToListAsync(cancellationToken);

        return CatgaResult<List<OrderDto>>.Success(orders);
    }
}

// ==================== Event Handlers ====================

/// <summary>
/// Order created event handler - Send notification
/// </summary>
public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedNotificationHandler> _logger;

    public OrderCreatedNotificationHandler(ILogger<OrderCreatedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📧 Sending notification: Order {@OrderNumber} created for {CustomerName}, Amount: ${TotalAmount}",
            @event.OrderNumber, @event.CustomerName, @event.TotalAmount);

        // TODO: Send email/SMS notification
        return Task.CompletedTask;
    }
}

/// <summary>
/// Order completed event handler - Update analytics
/// </summary>
public class OrderCompletedAnalyticsHandler : IEventHandler<OrderCompletedEvent>
{
    private readonly ILogger<OrderCompletedAnalyticsHandler> _logger;

    public OrderCompletedAnalyticsHandler(ILogger<OrderCompletedAnalyticsHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📊 Updating analytics: Order {@OrderNumber} completed at {CompletedAt}",
            @event.OrderNumber, @event.CompletedAt);

        // TODO: Update analytics/metrics
        return Task.CompletedTask;
    }
}

