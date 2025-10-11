# Catga.AspNetCore

ASP.NET Core integration for Catga CQRS framework.

Inspired by [CAP](https://github.com/dotnetcore/CAP)'s simple and elegant API design.

## Features

- 🚀 **Minimal API Extensions**: Map CQRS commands/queries to HTTP endpoints with one line
- 📊 **Built-in Diagnostics**: Health check and node info endpoints similar to CAP Dashboard
- 🎯 **Simple Integration**: Works seamlessly with `ICatgaMediator` pattern
- 📦 **Zero Config**: Auto-map diagnostics endpoints with `app.UseCatga()`

## Installation

```bash
dotnet add package Catga.AspNetCore
```

## Usage

### 1. Basic Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Catga
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

var app = builder.Build();

// Enable Catga diagnostics (health/node info)
app.UseCatga();

app.Run();
```

### 2. Map CQRS Endpoints (CAP Style)

```csharp
// Map Command - automatically handles ICatgaMediator injection and result wrapping
app.MapCatgaRequest<CreateOrderCommand, CreateOrderResult>("/api/orders");

// Map Query
app.MapCatgaQuery<GetOrderQuery, OrderDto>("/api/orders/{orderId}");

// Map Event
app.MapCatgaEvent<OrderCreatedEvent>("/api/events/order-created");
```

### 3. Manual ICatgaMediator Usage (Like CAP's ICapPublisher)

```csharp
app.MapPost("/api/orders", async (
    CreateOrderCommand command,
    ICatgaMediator mediator,  // Injected automatically
    CancellationToken ct) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command, ct);
    return result.ToHttpResult(); // Smart HTTP status code mapping
});
```

### 4. Smart Result Mapping

`ToHttpResult()` extension uses metadata to map `CatgaResult` to appropriate HTTP status codes:

```csharp
// In your handler, use factory methods to specify HTTP status:
public async Task<CatgaResult<OrderDto>> HandleAsync(GetOrderQuery request)
{
    var order = await _db.Orders.FindAsync(request.OrderId);
    
    if (order == null)
        return CatgaResultHttpExtensions.NotFound<OrderDto>("Order not found");
    
    if (order.Status == OrderStatus.Cancelled)
        return CatgaResultHttpExtensions.Conflict<OrderDto>("Order is already cancelled");
    
    return CatgaResult<OrderDto>.Success(orderDto);
}

// Or use Metadata directly (requires using Catga.Results):
var metadata = new ResultMetadata();
metadata.Add("ErrorType", "Validation");  // Maps to 422 Unprocessable Entity
return new CatgaResult<bool>
{
    IsSuccess = false,
    Error = "Invalid operation",
    Metadata = metadata
};

// Supported ErrorType values:
// - "NotFound" → 404 Not Found
// - "Conflict" → 409 Conflict  
// - "Validation" → 422 Unprocessable Entity
// - "Unauthorized" → 401 Unauthorized
// - "Forbidden" → 403 Forbidden

// Or specify HTTP status code directly:
return CatgaResult<T>.Failure("Error").WithStatusCode(503);
```

### 5. Built-in Diagnostics

Catga automatically adds these endpoints:

- `GET /catga/health` - Health check
- `GET /catga/node` - Node information (ID, machine, runtime, etc.)

You can customize the prefix:

```csharp
app.UseCatga(options =>
{
    options.DashboardPathPrefix = "/diagnostics";
    options.EnableDashboard = true;
});
```

## Comparison with CAP

| Feature | CAP | Catga |
|---------|-----|-------|
| Publisher Pattern | `ICapPublisher` | `ICatgaMediator` |
| Subscriber Pattern | `[CapSubscribe]` | `[CatgaHandler]` (Source Generator) |
| HTTP Integration | Manual | `MapCatgaRequest/Query/Event` |
| Dashboard | ✅ Full UI | ✅ API Endpoints |
| Message Transport | RabbitMQ, Kafka, etc. | NATS, Redis |
| Distributed Transactions | ✅ | ✅ (via Outbox/Inbox) |

## Example: OrderSystem

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Catga with NATS cluster
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddNatsCluster("nats://localhost:4222", "node-1", "http://localhost:5000");

var app = builder.Build();

// Enable Catga diagnostics
app.UseCatga();

// Map CQRS endpoints (one-liner style)
app.MapCatgaRequest<CreateOrderCommand, CreateOrderResult>("/api/orders");
app.MapCatgaRequest<ProcessOrderCommand, bool>("/api/orders/process");
app.MapCatgaQuery<GetOrderQuery, OrderDto>("/api/orders/{orderId}");

app.Run();
```

## AOT Compatibility

The `MapCatgaRequest/Query/Event` methods use ASP.NET Core's Minimal API which requires reflection.
These methods are marked with `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`.

For full AOT support, inject `ICatgaMediator` directly and use it manually:

```csharp
app.MapPost("/api/orders", async (
    CreateOrderCommand command,
    ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

## License

MIT

