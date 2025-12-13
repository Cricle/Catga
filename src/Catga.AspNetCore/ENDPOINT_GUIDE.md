# Catga AspNetCore Endpoint Guide

## Overview

Catga provides a source-generated endpoint registration system that is:
- **Zero Reflection** - All code generation happens at compile time
- **AOT Compatible** - Works with Native AOT compilation
- **Hot-Path Friendly** - Direct `MapPost/MapGet` calls, no middleware overhead
- **Simple** - Just mark methods with `[CatgaEndpoint]` attribute

## Quick Start

### 1. Define Handler Class with [CatgaEndpoint] Methods

```csharp
using Catga.Abstractions;
using Catga.AspNetCore;
using Microsoft.AspNetCore.Http;

public partial class OrderEndpointHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/orders", Name = "CreateOrder")]
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);

    [CatgaEndpoint(HttpMethod.Get, "/api/orders/{id}", Name = "GetOrder")]
    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator);
}
```

### 2. Implement Partial Methods

```csharp
public partial class OrderEndpointHandlers
{
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.Error });

        // Publish event
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderCreatedEvent { OrderId = result.Value.OrderId }
        }, 0);

        return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
    }

    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
    }
}
```

### 3. Register in Program.cs

```csharp
var app = builder.Build();

// Register endpoint handlers (source-generated, zero reflection)
app.RegisterEndpoint<OrderEndpointHandlers>();

app.Run();
```

## How It Works

### Source Generator

The `EndpointRegistrationGenerator` scans for methods marked with `[CatgaEndpoint]` and generates a `RegisterEndpoints` static method:

```csharp
// Generated code (OrderEndpointHandlers.Endpoints.g.cs)
public partial class OrderEndpointHandlers
{
    public static void RegisterEndpoints(WebApplication app)
    {
        app.MapPost("/api/orders", CreateOrder)
            .WithName("CreateOrder");

        app.MapGet("/api/orders/{id}", GetOrder)
            .WithName("GetOrder");
    }
}
```

### Registration Flow

1. User marks methods with `[CatgaEndpoint]` attribute
2. Source generator creates `RegisterEndpoints` method
3. User calls `app.RegisterEndpoint<THandler>()` in Program.cs
4. Extension method calls `THandler.RegisterEndpoints(app)`
5. All endpoints are registered with zero reflection

## Features

### Attribute Options

```csharp
[CatgaEndpoint(
    httpMethod: "Post",           // HTTP method
    route: "/api/orders",         // Route pattern
    Name = "CreateOrder",         // Optional: Endpoint name
    Description = "Create order"  // Optional: Description
)]
public partial async Task<IResult> CreateOrder(...);
```

### Fluent Chaining

Register multiple handlers in a chain:

```csharp
app.RegisterEndpoint<OrderEndpointHandlers>()
   .RegisterEndpoint<PaymentEndpointHandlers>()
   .RegisterEndpoint<ShippingEndpointHandlers>();
```

### Request/Response Binding

The source generator automatically detects:
- **Request Parameter** - First parameter that is not `ICatgaMediator` or `IEventStore`
- **HTTP Method** - From attribute (Post, Get, Put, Delete, Patch)
- **Route** - From attribute pattern

```csharp
[CatgaEndpoint(HttpMethod.Post, "/api/orders")]
public partial async Task<IResult> CreateOrder(
    CreateOrderCommand cmd,      // Request parameter (auto-detected)
    ICatgaMediator mediator,     // Injected by ASP.NET Core
    IEventStore eventStore)      // Injected by ASP.NET Core
{
    // Implementation
}
```

## Best Practices

### 1. Separate Declaration and Implementation

```csharp
// Declaration with attributes
public partial class OrderEndpointHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/orders")]
    public partial async Task<IResult> CreateOrder(...);
}

// Implementation in separate file
public partial class OrderEndpointHandlers
{
    public partial async Task<IResult> CreateOrder(...)
    {
        // Implementation
    }
}
```

### 2. Use Consistent Naming

- Handler class: `{Entity}EndpointHandlers`
- Method: `{Action}{Entity}` (e.g., `CreateOrder`, `GetOrder`)
- Endpoint name: Same as method name

### 3. Handle Errors Consistently

```csharp
public partial async Task<IResult> CreateOrder(...)
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(new { error = result.Error });

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

### 4. Publish Events After Success

```csharp
public partial async Task<IResult> CreateOrder(...)
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);

    if (!result.IsSuccess)
        return Results.BadRequest(result.Error);

    // Only publish if command succeeded
    await eventStore.AppendAsync("orders", new IEvent[]
    {
        new OrderCreatedEvent { OrderId = result.Value.OrderId }
    }, 0);

    return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
}
```

## AOT Compatibility

This endpoint system is fully AOT-compatible:

- ✅ Zero reflection at runtime
- ✅ All code generation at compile time
- ✅ No dynamic code generation
- ✅ Works with Native AOT compilation

## Performance

- **Zero Overhead** - Direct `MapPost/MapGet` calls
- **No Middleware** - Handlers are called directly
- **Minimal Allocations** - Source-generated code is optimized
- **Hot-Path Friendly** - Follows ASP.NET Core best practices

## Troubleshooting

### RegisterEndpoints Method Not Found

**Issue**: `THandler.RegisterEndpoints` method not found

**Solution**:
1. Ensure handler class is `partial`
2. Ensure at least one method has `[CatgaEndpoint]` attribute
3. Rebuild solution to trigger source generator
4. Check `obj/Debug/net9.0/generated/` for generated files

### Attribute Not Recognized

**Issue**: `CatgaEndpointAttribute` not found

**Solution**:
1. Add `using Catga.AspNetCore;`
2. Ensure `Catga.AspNetCore` package is referenced

### Endpoint Not Registered

**Issue**: Endpoint not accessible

**Solution**:
1. Verify `app.RegisterEndpoint<THandler>()` is called in Program.cs
2. Check route pattern is correct
3. Verify handler class is `partial`
4. Check method is `partial` and has `[CatgaEndpoint]` attribute

## See Also

- [Catga CQRS Framework](../README.md)
- [AspNetCore Integration](./README.md)
- [OrderSystem.Api Example](../../examples/OrderSystem.Api/)
