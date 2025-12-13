# OrderSystem.Api - Best Practices Guide

## Overview

This guide demonstrates best practices for building production-ready endpoints using Catga AspNetCore Endpoints. The OrderSystem.Api example shows patterns for validation, error handling, logging, caching, and event publishing.

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

    // Continue with business logic
}
```

### Pattern 2: Extension Method Validation

```csharp
public partial async Task<IResult> PayOrder(
    PayOrderCommand cmd,
    ICatgaMediator mediator)
{
    // Validate using extension methods
    var (isValid, error) = cmd.OrderId.ValidateRequired("OrderId");
    if (!isValid)
        return Results.BadRequest(new { error });

    var (isValid2, error2) = cmd.Amount.ValidatePositive("Amount");
    if (!isValid2)
        return Results.BadRequest(new { error = error2 });

    // Continue with business logic
}
```

### Pattern 3: Multiple Validators

```csharp
public partial async Task<IResult> CreateOrder(CreateOrderCommand cmd, ...)
{
    var (isValid, error) = cmd.ValidateMultiple(
        c => c.CustomerId.ValidateRequired("CustomerId"),
        c => c.CustomerId.ValidateMinLength(3, "CustomerId"),
        c => c.Items.ValidateNotEmpty("Items"),
        c => c.Items.ValidateMinCount(1, "Items")
    );

    if (!isValid)
        return Results.BadRequest(new { error });

    // Continue with business logic
}
```

## 2. Error Handling Patterns

### Pattern 1: Comprehensive Error Mapping

```csharp
public partial async Task<IResult> PayOrder(
    PayOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<PayOrderCommand, bool>(cmd);

    if (!result.IsSuccess)
    {
        return result.Error switch
        {
            var e when e.Contains("NotFound") => Results.NotFound(),
            var e when e.Contains("Conflict") => Results.Conflict(new { error = e }),
            var e when e.Contains("Validation") => Results.BadRequest(new { error = e }),
            _ => Results.BadRequest(new { error = result.Error })
        };
    }

    // Continue with business logic
}
```

### Pattern 2: Consistent Error Response

```csharp
// All errors follow consistent format
return Results.BadRequest(new { error = result.Error });
return Results.NotFound();
return Results.Conflict(new { error = "Resource conflict" });
return Results.StatusCode(StatusCodes.Status500InternalServerError);
```

### Pattern 3: Error Context Preservation

```csharp
try
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);
    // Handle result
}
catch (TimeoutException)
{
    _logger.LogError("Command execution timeout");
    return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error");
    return Results.StatusCode(StatusCodes.Status500InternalServerError);
}
```

## 3. Logging Patterns

### Pattern 1: Request/Response Logging

```csharp
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    _logger.LogInformation("Creating order for customer {CustomerId}", cmd.CustomerId);

    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
    {
        _logger.LogWarning("Order creation failed: {Error}", result.Error);
        return Results.BadRequest(result.Error);
    }

    _logger.LogInformation("Order {OrderId} created successfully", result.Value.OrderId);
    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

### Pattern 2: Performance Monitoring

```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    stopwatch.Stop();
    _logger.LogInformation("GetOrder completed in {ElapsedMilliseconds}ms",
        stopwatch.ElapsedMilliseconds);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
}
```

## 4. Caching Patterns

### Pattern 1: Memory Cache Integration

```csharp
public partial async Task<IResult> GetOrder(
    GetOrderQuery query,
    ICatgaMediator mediator,
    IMemoryCache cache)
{
    var cacheKey = $"order_{query.Id}";

    // Check cache first
    if (cache.TryGetValue(cacheKey, out Order? cachedOrder))
    {
        _logger.LogInformation("Order {OrderId} found in cache", query.Id);
        return Results.Ok(cachedOrder);
    }

    // Execute query
    var result = await mediator.SendAsync<GetOrderQuery, Order?>(query);

    if (result.IsSuccess && result.Value != null)
    {
        // Cache for 5 minutes
        cache.Set(cacheKey, result.Value, TimeSpan.FromMinutes(5));
    }

    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
}
```

### Pattern 2: Cache Invalidation

```csharp
public partial async Task<IResult> PayOrder(
    PayOrderCommand cmd,
    ICatgaMediator mediator,
    IMemoryCache cache)
{
    var result = await mediator.SendAsync<PayOrderCommand, bool>(cmd);

    if (result.IsSuccess)
    {
        // Invalidate cache
        cache.Remove($"order_{cmd.OrderId}");
        _logger.LogInformation("Cache invalidated for order {OrderId}", cmd.OrderId);
    }

    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}
```

## 5. Event Publishing Patterns

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

### Pattern 2: Error Handling in Event Publishing

```csharp
public partial async Task<IResult> CreateOrder(...)
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Publish event with error handling
    try
    {
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderCreatedEvent { OrderId = result.Value.OrderId }
        }, 0);

        _logger.LogInformation("Event published for order {OrderId}", result.Value.OrderId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish event for order {OrderId}", result.Value.OrderId);
        // Don't fail the request if event publishing fails
    }

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

## 6. Input Sanitization Patterns

### Pattern 1: HTML Encoding

```csharp
public partial async Task<IResult> SearchOrders(
    SearchOrdersQuery query,
    ICatgaMediator mediator)
{
    // Sanitize input
    query.SearchTerm = System.Web.HttpUtility.HtmlEncode(query.SearchTerm);

    var result = await mediator.SendAsync<SearchOrdersQuery, List<Order>>(query);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}
```

### Pattern 2: Input Validation and Limits

```csharp
public partial async Task<IResult> SearchOrders(
    SearchOrdersQuery query,
    ICatgaMediator mediator)
{
    // Sanitize and validate
    query.SearchTerm = System.Web.HttpUtility.HtmlEncode(query.SearchTerm);
    query.PageSize = Math.Min(query.PageSize, 100); // Limit page size
    query.PageNumber = Math.Max(query.PageNumber, 1); // Ensure valid page

    var result = await mediator.SendAsync<SearchOrdersQuery, List<Order>>(query);

    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}
```

## 7. Batch Processing Patterns

### Pattern 1: Batch Validation

```csharp
public partial async Task<IResult> CreateOrdersBatch(
    CreateOrdersBatchCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    // Validate batch
    var validation = new ValidationBuilder()
        .AddErrorIf(cmd.Orders?.Count == 0, "Batch must contain at least one order")
        .AddErrorIf(cmd.Orders?.Count > 100, "Batch cannot exceed 100 orders");

    if (!validation.IsValid)
        return validation.ToResult();

    // Continue with processing
}
```

### Pattern 2: Batch Event Publishing

```csharp
public partial async Task<IResult> CreateOrdersBatch(
    CreateOrdersBatchCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    var result = await mediator.SendAsync<CreateOrdersBatchCommand, BatchResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Publish multiple events
    var events = result.Value.CreatedOrderIds
        .Select(orderId => new OrderCreatedEvent
        {
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow
        })
        .Cast<IEvent>()
        .ToArray();

    await eventStore.AppendAsync("orders", events, 0);

    return Results.Created("/api/orders", result.Value);
}
```

## 8. Response Mapping Patterns

### Pattern 1: Status Code Based on Operation

```csharp
// Create - 201 Created
return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);

// Read - 200 OK
return Results.Ok(result.Value);

// Update - 200 OK
return Results.Ok(result.Value);

// Delete - 204 No Content
return Results.NoContent();

// Not Found - 404
return Results.NotFound();

// Bad Request - 400
return Results.BadRequest(new { error = result.Error });
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

## 9. Concurrency Patterns

### Pattern 1: Handling Concurrent Requests

```csharp
// Endpoints automatically handle concurrent requests
// ASP.NET Core manages request threading
// Catga mediator is thread-safe
// Event store handles concurrent appends

// No special handling needed - just implement endpoint logic
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,
    ICatgaMediator mediator,
    IEventStore eventStore)
{
    // This method can be called concurrently
    // All dependencies are thread-safe
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);
    // ...
}
```

### Pattern 2: Idempotency

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
            IdempotencyKey = idempotencyKey
        }
    }, 0);

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

## 10. Testing Patterns

### Pattern 1: Unit Testing Validation

```csharp
[Fact]
public async Task CreateOrder_ShouldReturnBadRequest_WhenCustomerIdIsEmpty()
{
    // Arrange
    var handler = new OrderEndpointHandlersBestPractices(_logger);
    var cmd = new CreateOrderCommand { CustomerId = "" };
    var mediator = new MockCatgaMediator();
    var eventStore = new MockEventStore();

    // Act
    var result = await handler.CreateOrder(cmd, mediator, eventStore);

    // Assert
    result.Should().BeOfType<BadRequestObjectResult>();
}
```

### Pattern 2: Integration Testing Complete Flow

```csharp
[Fact]
public async Task CompleteOrderWorkflow_ShouldSucceed()
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

## Summary

These patterns provide:
- ✅ Comprehensive validation
- ✅ Proper error handling
- ✅ Effective logging
- ✅ Smart caching
- ✅ Event publishing
- ✅ Input sanitization
- ✅ Batch processing
- ✅ Correct HTTP status codes
- ✅ Concurrency safety
- ✅ Testability

Use these patterns as templates for building production-ready endpoints.
