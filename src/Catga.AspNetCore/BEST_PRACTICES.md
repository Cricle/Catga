# Catga AspNetCore Endpoint Best Practices

## 1. Validation Patterns

### Pattern 1: Fluent Validation Builder
```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    // Validate using fluent builder
    var validation = new ValidationBuilder()
        .AddErrorIf(string.IsNullOrEmpty(cmd.CustomerId), "CustomerId is required")
        .AddErrorIf(cmd.Items?.Count == 0, "Order must have at least one item")
        .AddErrorIf(cmd.Items?.Any(i => i.Price <= 0) ?? false, "Item prices must be positive");

    if (!validation.IsValid)
        return validation.ToResult();

    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);
    return result.IsSuccess ? Results.Created(...) : Results.BadRequest(result.Error);
}
```

### Pattern 2: Extension Method Validation
```csharp
public partial async Task<IResult> UpdateOrder(
    UpdateOrderCommand cmd,
    ICatgaMediator mediator)
{
    // Validate using extension methods
    var (isValid, error) = cmd.ValidateMultiple(
        c => c.OrderId.ValidateRequired("OrderId"),
        c => c.OrderId.ValidateMinLength(3, "OrderId"),
        c => c.Amount.ValidatePositive("Amount")
    );

    if (!isValid)
        return Results.BadRequest(new { error });

    var result = await mediator.SendAsync<UpdateOrderCommand, OrderResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}
```

## 2. Error Handling Patterns

### Pattern 1: Comprehensive Error Handling
```csharp
public partial async Task<IResult> PayOrder(
    PayOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    try
    {
        var result = await mediator.SendAsync<PayOrderCommand, bool>(cmd);

        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                var e when e.Contains("NotFound") => Results.NotFound(),
                var e when e.Contains("Conflict") => Results.Conflict(),
                var e when e.Contains("Validation") => Results.BadRequest(new { error = e }),
                _ => Results.BadRequest(new { error = result.Error })
            };
        }

        // Publish event on success
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderPaidEvent { OrderId = cmd.OrderId, PaidAt = DateTime.UtcNow }
        }, 0);

        return Results.Ok();
    }
    catch (TimeoutException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (Exception ex)
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
}
```

### Pattern 2: Consistent Error Response
```csharp
public partial async Task<IResult> DeleteOrder(
    DeleteOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<DeleteOrderCommand, bool>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(new { error = result.Error, timestamp = DateTime.UtcNow });

    await eventStore.AppendAsync("orders", new IEvent[]
    {
        new OrderDeletedEvent { OrderId = cmd.Id, DeletedAt = DateTime.UtcNow }
    }, 0);

    return Results.NoContent();
}
```

## 3. Event Publishing Patterns

### Pattern 1: Event Publishing with Metadata
```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Publish event with full metadata
    var @event = new OrderCreatedEvent
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
    };

    await eventStore.AppendAsync("orders", new IEvent[] { @event }, 0);

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

### Pattern 2: Batch Event Publishing
```csharp
public partial async Task<IResult> ProcessOrders(
    ProcessOrdersCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<ProcessOrdersCommand, ProcessResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Publish multiple events
    var events = result.Value.ProcessedOrders
        .Select(o => new OrderProcessedEvent
        {
            OrderId = o.OrderId,
            ProcessedAt = DateTime.UtcNow,
            Status = o.Status
        })
        .Cast<IEvent>()
        .ToArray();

    await eventStore.AppendAsync("orders", events, 0);

    return Results.Ok(result.Value);
}
```

## 4. Response Mapping Patterns

### Pattern 1: Status Code Based on Operation
```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // 201 Created for new resource
    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}

public partial async Task<IResult> UpdateOrder(
    UpdateOrderCommand cmd,
    ICatgaMediator mediator)
{
    var result = await mediator.SendAsync<UpdateOrderCommand, OrderResult>(cmd);

    // 200 OK for update
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}

public partial async Task<IResult> DeleteOrder(
    DeleteOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<DeleteOrderCommand, bool>(cmd);

    // 204 No Content for delete
    return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
}
```

### Pattern 2: Conditional Response Mapping
```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator)
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderDto?>(query);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    if (result.Value == null)
        return Results.NotFound();

    return Results.Ok(result.Value);
}
```

## 5. Concurrency and Idempotency Patterns

### Pattern 1: Idempotent Command Handling
```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    // Use idempotency key to prevent duplicate processing
    var idempotencyKey = cmd.IdempotencyKey ?? Guid.NewGuid().ToString();

    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Store idempotency key with result
    await eventStore.AppendAsync("orders", new IEvent[]
    {
        new OrderCreatedEvent
        {
            OrderId = result.Value.OrderId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        }
    }, 0);

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

### Pattern 2: Optimistic Concurrency Control
```csharp
public partial async Task<IResult> UpdateOrder(
    UpdateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<UpdateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
    {
        // Check if conflict due to concurrency
        if (result.Error?.Contains("ConcurrencyException") ?? false)
            return Results.Conflict(new { error = "Order was modified by another request" });

        return Results.BadRequest(result.Error);
    }

    await eventStore.AppendAsync("orders", new IEvent[]
    {
        new OrderUpdatedEvent
        {
            OrderId = cmd.Id,
            Version = cmd.Version,
            UpdatedAt = DateTime.UtcNow
        }
    }, 0);

    return Results.Ok(result.Value);
}
```

## 6. Logging and Monitoring Patterns

### Pattern 1: Request/Response Logging
```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore,
    ILogger<OrderEndpointHandlers> logger)
{
    logger.LogInformation("Creating order for customer {CustomerId}", cmd.CustomerId);

    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
    {
        logger.LogWarning("Order creation failed: {Error}", result.Error);
        return Results.BadRequest(result.Error);
    }

    logger.LogInformation("Order {OrderId} created successfully", result.Value.OrderId);

    await eventStore.AppendAsync("orders", new IEvent[]
    {
        new OrderCreatedEvent { OrderId = result.Value.OrderId }
    }, 0);

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

### Pattern 2: Performance Monitoring
```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator,
    ILogger<OrderEndpointHandlers> logger)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    stopwatch.Stop();
    logger.LogInformation("GetOrder completed in {ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
}
```

## 7. Security Patterns

### Pattern 1: Authorization Check
```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator,
    HttpContext httpContext)
{
    // Check authorization
    var userId = httpContext.User.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    if (result.IsSuccess && result.Value?.CustomerId != userId)
        return Results.Forbid();

    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
}
```

### Pattern 2: Input Sanitization
```csharp
public partial async Task<IResult> SearchOrders(
    SearchOrdersQuery query,
    ICatgaMediator mediator)
{
    // Sanitize search input
    query.SearchTerm = System.Web.HttpUtility.HtmlEncode(query.SearchTerm);
    query.PageSize = Math.Min(query.PageSize, 100); // Limit page size

    var result = await mediator.SendAsync<SearchOrdersQuery, List<OrderDto>>(query);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}
```

## 8. Testing Patterns

### Pattern 1: Unit Test for Validation
```csharp
[Fact]
public async Task CreateOrder_ShouldReturnBadRequest_WhenCustomerIdIsEmpty()
{
    // Arrange
    var handler = new OrderEndpointHandlers();
    var cmd = new CreateOrderCommand { CustomerId = "" };
    var mediator = new MockCatgaMediator();
    var eventStore = new MockEventStore();

    // Act
    var result = await handler.CreateOrder(cmd, mediator, eventStore);

    // Assert
    result.Should().BeOfType<BadRequestObjectResult>();
}
```

### Pattern 2: Integration Test for Complete Flow
```csharp
[Fact]
public async Task CompleteOrderLifecycle_ShouldSucceed()
{
    // Arrange
    var client = CreateTestClient();

    // Act - Create
    var createResponse = await client.PostAsJsonAsync("/api/orders", new CreateOrderCommand { ... });
    var orderId = (await createResponse.Content.ReadAsAsync<OrderCreatedResult>()).OrderId;

    // Act - Get
    var getResponse = await client.GetAsync($"/api/orders/{orderId}");

    // Act - Pay
    var payResponse = await client.PutAsJsonAsync($"/api/orders/{orderId}/pay", new PayOrderCommand { ... });

    // Assert
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    payResponse.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

## 9. Performance Optimization Patterns

### Pattern 1: Caching Query Results
```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator,
    IMemoryCache cache)
{
    var cacheKey = $"order_{query.Id}";

    if (cache.TryGetValue(cacheKey, out OrderDto? cachedOrder))
        return Results.Ok(cachedOrder);

    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    if (result.IsSuccess && result.Value != null)
        cache.Set(cacheKey, result.Value, TimeSpan.FromMinutes(5));

    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
}
```

### Pattern 2: Batch Processing
```csharp
public partial async Task<IResult> GetOrders(
    GetOrdersQuery query,
    ICatgaMediator mediator)
{
    // Limit batch size for performance
    query.PageSize = Math.Min(query.PageSize, 100);

    var result = await mediator.SendAsync<GetOrdersQuery, List<OrderDto>>(query);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}
```

## 10. Documentation Patterns

### Pattern 1: XML Documentation for Endpoints
```csharp
/// <summary>
/// Create a new order.
/// </summary>
/// <param name="cmd">The create order command containing customer and item details.</param>
/// <param name="mediator">The Catga mediator for command execution.</param>
/// <param name="eventStore">The event store for publishing order events.</param>
/// <returns>201 Created with order details, or 400 Bad Request if validation fails.</returns>
[CatgaEndpoint(HttpMethod.Post, "/api/orders", Name = "CreateOrder", Description = "Create a new order")]
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore);
```

## Summary

These patterns provide:
- ✅ Consistent validation approach
- ✅ Comprehensive error handling
- ✅ Proper event publishing
- ✅ Correct HTTP status codes
- ✅ Concurrency safety
- ✅ Security best practices
- ✅ Performance optimization
- ✅ Testability
- ✅ Logging and monitoring
- ✅ Clear documentation
