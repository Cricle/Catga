using Catga.Abstractions;
using Catga.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Order endpoint handlers demonstrating best practices.
/// Includes validation, error handling, logging, and event publishing.
/// </summary>
public partial class OrderEndpointHandlersBestPractices
{
    private readonly ILogger<OrderEndpointHandlersBestPractices> _logger;

    public OrderEndpointHandlersBestPractices(ILogger<OrderEndpointHandlersBestPractices> logger)
    {
        _logger = logger;
    }

    [CatgaEndpoint(HttpMethod.Post, "/api/orders/best-practice", Name = "CreateOrderBestPractice")]
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore,
        HttpContext httpContext);

    [CatgaEndpoint(HttpMethod.Get, "/api/orders/best-practice/{id}", Name = "GetOrderBestPractice")]
    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator,
        IMemoryCache cache);

    [CatgaEndpoint(HttpMethod.Put, "/api/orders/best-practice/{id}/pay", Name = "PayOrderBestPractice")]
    public partial async Task<IResult> PayOrder(
        PayOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);

    [CatgaEndpoint(HttpMethod.Get, "/api/orders/best-practice/search", Name = "SearchOrdersBestPractice")]
    public partial async Task<IResult> SearchOrders(
        SearchOrdersQuery query,
        ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Post, "/api/orders/best-practice/batch", Name = "CreateOrdersBatchBestPractice")]
    public partial async Task<IResult> CreateOrdersBatch(
        CreateOrdersBatchCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);
}

/// <summary>
/// Implementation with best practices.
/// </summary>
public partial class OrderEndpointHandlersBestPractices
{
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore,
        HttpContext httpContext)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", cmd.CustomerId);

        // Validate request
        var validation = new ValidationBuilder()
            .AddErrorIf(string.IsNullOrEmpty(cmd.CustomerId), "CustomerId is required")
            .AddErrorIf(cmd.Items?.Count == 0, "Order must have at least one item")
            .AddErrorIf(cmd.Items?.Any(i => i.Price <= 0) ?? false, "Item prices must be positive");

        if (!validation.IsValid)
        {
            _logger.LogWarning("Order creation validation failed: {Errors}", string.Join(", ", validation.Errors));
            return validation.ToResult();
        }

        // Execute command
        var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Order creation failed: {Error}", result.Error);
            return Results.BadRequest(new { error = result.Error });
        }

        // Publish event
        try
        {
            await eventStore.AppendAsync("orders", new IEvent[]
            {
                new OrderCreatedEvent
                {
                    OrderId = result.Value.OrderId,
                    CustomerId = cmd.CustomerId,
                    TotalAmount = cmd.Items.Sum(i => i.Subtotal),
                    CreatedAt = DateTime.UtcNow,
                    Items = cmd.Items.Select(i => new OrderItemSnapshot
                    {
                        ProductId = i.ProductId,
                        Quantity = i.Quantity,
                        Price = i.Price
                    }).ToList()
                }
            }, 0);

            _logger.LogInformation("Order {OrderId} created successfully", result.Value.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish order created event for {OrderId}", result.Value.OrderId);
            // Don't fail the request if event publishing fails, but log it
        }

        return Results.Created($"/api/orders/best-practice/{result.Value.OrderId}", result.Value);
    }

    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator,
        IMemoryCache cache)
    {
        _logger.LogInformation("Getting order {OrderId}", query.Id);

        // Validate
        var (isValid, error) = query.Id.ValidateRequired("OrderId");
        if (!isValid)
            return Results.BadRequest(new { error });

        // Check cache
        var cacheKey = $"order_{query.Id}";
        if (cache.TryGetValue(cacheKey, out Order? cachedOrder))
        {
            _logger.LogInformation("Order {OrderId} found in cache", query.Id);
            return Results.Ok(cachedOrder);
        }

        // Execute query
        var result = await mediator.SendAsync<GetOrderQuery, Order?>(query);

        if (!result.IsSuccess || result.Value == null)
        {
            _logger.LogWarning("Order {OrderId} not found", query.Id);
            return Results.NotFound();
        }

        // Cache result
        cache.Set(cacheKey, result.Value, TimeSpan.FromMinutes(5));

        _logger.LogInformation("Order {OrderId} retrieved successfully", query.Id);
        return Results.Ok(result.Value);
    }

    public partial async Task<IResult> PayOrder(
        PayOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        _logger.LogInformation("Paying order {OrderId} with amount {Amount}", cmd.OrderId, cmd.Amount);

        // Validate
        var validation = new ValidationBuilder()
            .AddErrorIf(string.IsNullOrEmpty(cmd.OrderId), "OrderId is required")
            .AddErrorIf(cmd.Amount <= 0, "Amount must be positive");

        if (!validation.IsValid)
        {
            _logger.LogWarning("Order payment validation failed: {Errors}", string.Join(", ", validation.Errors));
            return validation.ToResult();
        }

        // Execute command
        var result = await mediator.SendAsync<PayOrderCommand, bool>(cmd);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Order payment failed: {Error}", result.Error);

            // Map error to appropriate HTTP status
            return result.Error switch
            {
                var e when e.Contains("NotFound") => Results.NotFound(),
                var e when e.Contains("Conflict") => Results.Conflict(new { error = e }),
                _ => Results.BadRequest(new { error = result.Error })
            };
        }

        // Publish event
        try
        {
            await eventStore.AppendAsync("orders", new IEvent[]
            {
                new OrderPaidEvent
                {
                    OrderId = cmd.OrderId,
                    Amount = cmd.Amount,
                    PaidAt = DateTime.UtcNow
                }
            }, 0);

            _logger.LogInformation("Order {OrderId} paid successfully", cmd.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish order paid event for {OrderId}", cmd.OrderId);
        }

        return Results.Ok(new { message = "Order paid successfully", orderId = cmd.OrderId });
    }

    public partial async Task<IResult> SearchOrders(
        SearchOrdersQuery query,
        ICatgaMediator mediator)
    {
        _logger.LogInformation("Searching orders with term {SearchTerm}", query.SearchTerm);

        // Sanitize input
        query.SearchTerm = System.Web.HttpUtility.HtmlEncode(query.SearchTerm);
        query.PageSize = Math.Min(query.PageSize, 100); // Limit page size
        query.PageNumber = Math.Max(query.PageNumber, 1); // Ensure valid page

        // Execute query
        var result = await mediator.SendAsync<SearchOrdersQuery, List<Order>>(query);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Order search failed: {Error}", result.Error);
            return Results.BadRequest(new { error = result.Error });
        }

        _logger.LogInformation("Found {Count} orders", result.Value.Count);
        return Results.Ok(result.Value);
    }

    public partial async Task<IResult> CreateOrdersBatch(
        CreateOrdersBatchCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        _logger.LogInformation("Creating batch of {Count} orders", cmd.Orders.Count);

        // Validate batch
        var validation = new ValidationBuilder()
            .AddErrorIf(cmd.Orders?.Count == 0, "Batch must contain at least one order")
            .AddErrorIf(cmd.Orders?.Count > 100, "Batch cannot exceed 100 orders");

        if (!validation.IsValid)
        {
            _logger.LogWarning("Batch creation validation failed: {Errors}", string.Join(", ", validation.Errors));
            return validation.ToResult();
        }

        // Execute batch command
        var result = await mediator.SendAsync<CreateOrdersBatchCommand, BatchCreatedResult>(cmd);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Batch creation failed: {Error}", result.Error);
            return Results.BadRequest(new { error = result.Error });
        }

        // Publish events for all created orders
        try
        {
            var events = result.Value.CreatedOrderIds
                .Select(orderId => new OrderCreatedEvent
                {
                    OrderId = orderId,
                    CreatedAt = DateTime.UtcNow
                })
                .Cast<IEvent>()
                .ToArray();

            await eventStore.AppendAsync("orders", events, 0);

            _logger.LogInformation("Batch of {Count} orders created successfully", result.Value.CreatedOrderIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch creation events");
        }

        return Results.Created("/api/orders/best-practice", result.Value);
    }
}

// Supporting types for best practices
public class OrderItemSnapshot
{
    public string ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class BatchCreatedResult
{
    public List<string> CreatedOrderIds { get; set; } = new();
}

public class SearchOrdersQuery : IRequest<List<Order>>
{
    public string SearchTerm { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class CreateOrdersBatchCommand : IRequest<BatchCreatedResult>
{
    public List<CreateOrderCommand> Orders { get; set; } = new();
}
