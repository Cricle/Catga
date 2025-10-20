# Error Handling Guide

## Philosophy: Error Codes > Exceptions

Catga uses **structured error codes** instead of throwing exceptions for business logic errors.

### Why Error Codes?

| Aspect | Exceptions | Error Codes |
|--------|-----------|-------------|
| **Performance** | ~2-5μs overhead | ~5ns overhead |
| **Memory** | Heap allocation | Stack allocation |
| **Control Flow** | Implicit (stack unwinding) | Explicit (return value) |
| **Recovery** | Harder to handle | Easier to match and retry |
| **Debuggability** | Stack trace | Error code + context |

---

## Error Code Structure

All Catga error codes follow the format: `CATGA_XXXX`

```csharp
public static class ErrorCodes
{
    // Message Processing (1xxx)
    public const string MessageValidationFailed = "CATGA_1001";
    public const string InvalidMessageId = "CATGA_1002";
    public const string MessageAlreadyProcessed = "CATGA_1003";
    
    // Inbox/Outbox (2xxx)
    public const string InboxLockFailed = "CATGA_2001";
    public const string InboxPersistenceFailed = "CATGA_2002";
    
    // Transport (3xxx)
    public const string TransportPublishFailed = "CATGA_3002";
    
    // Persistence (4xxx)
    public const string EventStoreWriteFailed = "CATGA_4001";
    
    // Configuration (5xxx)
    public const string SerializerNotRegistered = "CATGA_5001";
    
    // Pipeline (6xxx)
    public const string HandlerExecutionFailed = "CATGA_6003";
}
```

---

## Using ErrorInfo

### Creating Errors

```csharp
// From exception
var error = ErrorInfo.FromException(ex, ErrorCodes.InboxPersistenceFailed, isRetryable: true);

// Validation error
var error = ErrorInfo.Validation("Invalid email format", "email: must contain @");

// Timeout error
var error = ErrorInfo.Timeout("Operation timed out after 5 seconds");

// Not found error
var error = ErrorInfo.NotFound("User not found");

// Configuration error
var error = ErrorInfo.Configuration("Redis connection string is missing");

// Custom error
var error = new ErrorInfo
{
    Code = ErrorCodes.CustomError,
    Message = "Something went wrong",
    IsRetryable = false,
    Details = "Additional context here"
};
```

---

## Returning Errors from Handlers

### ❌ Bad: Throwing Exceptions

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrder, CatgaResult<OrderId>>
{
    public async Task<CatgaResult<OrderId>> HandleAsync(CreateOrder request, CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be > 0");  // ❌ Slow, implicit
        
        var order = await _orderService.CreateAsync(request);
        return CatgaResult<OrderId>.Success(order.Id);
    }
}
```

### ✅ Good: Returning ErrorInfo

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrder, CatgaResult<OrderId>>
{
    public async Task<CatgaResult<OrderId>> HandleAsync(CreateOrder request, CancellationToken ct)
    {
        if (request.Amount <= 0)
            return CatgaResult<OrderId>.Failure(ErrorInfo.Validation(
                "Amount must be > 0", 
                $"Amount: {request.Amount}"));  // ✅ Fast, explicit
        
        var order = await _orderService.CreateAsync(request);
        return CatgaResult<OrderId>.Success(order.Id);
    }
}
```

---

## Handling Errors in Application Code

### Pattern Matching

```csharp
var result = await _mediator.SendAsync(new CreateOrder { Amount = 100 });

if (!result.IsSuccess)
{
    // Check error code
    switch (result.ErrorCode)
    {
        case ErrorCodes.MessageValidationFailed:
            _logger.LogWarning("Validation failed: {Error}", result.Error);
            return BadRequest(result.Error);
        
        case ErrorCodes.InboxPersistenceFailed:
            if (result.IsRetryable)  // Check if retryable (from ErrorInfo)
            {
                _logger.LogWarning("Retryable error: {Error}", result.Error);
                // Enqueue for retry
                await _retryQueue.EnqueueAsync(request);
            }
            return StatusCode(503, "Service temporarily unavailable");
        
        case ErrorCodes.HandlerExecutionFailed:
            _logger.LogError("Handler failed: {Error}", result.Error);
            return StatusCode(500, "Internal server error");
        
        default:
            _logger.LogError("Unknown error: {ErrorCode} - {Error}", result.ErrorCode, result.Error);
            return StatusCode(500, "Internal server error");
    }
}

// Success
var orderId = result.Value;
return Ok(new { OrderId = orderId });
```

### Retry Logic

```csharp
var result = await _mediator.SendAsync(request);

if (!result.IsSuccess)
{
    // Extract ErrorInfo-like properties from result
    var isRetryable = result.ErrorCode switch
    {
        ErrorCodes.InboxLockFailed => true,
        ErrorCodes.InboxPersistenceFailed => true,
        ErrorCodes.TransportPublishFailed => true,
        ErrorCodes.NetworkTimeout => true,
        _ => false
    };
    
    if (isRetryable && retryCount < maxRetries)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
        return await RetryAsync(request, retryCount + 1, maxRetries);
    }
    
    return result;
}

return result;
```

---

## ErrorInfo in CatgaResult

`CatgaResult` now includes `ErrorCode`:

```csharp
public readonly record struct CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }  // ✨ New
    public CatgaException? Exception { get; init; }
}
```

### Creating Results

```csharp
// From ErrorInfo (recommended)
var error = ErrorInfo.Validation("Invalid input");
return CatgaResult<OrderId>.Failure(error);

// From exception (legacy)
return CatgaResult<OrderId>.Failure("Error message", catgaException);

// Manual (not recommended)
return new CatgaResult<OrderId>
{
    IsSuccess = false,
    Error = "Error message",
    ErrorCode = ErrorCodes.CustomError
};
```

---

## Exception vs Error Code Decision Tree

```
Is this a programming error?
├─ Yes → Use Exception (ArgumentException, InvalidOperationException)
│  Examples: null argument, invalid state, configuration error
│
└─ No → Use ErrorInfo
   ├─ Is it a validation error?
   │  └─ Use ErrorInfo.Validation(...)
   │
   ├─ Is it a timeout?
   │  └─ Use ErrorInfo.Timeout(...)
   │
   ├─ Is it a not found error?
   │  └─ Use ErrorInfo.NotFound(...)
   │
   └─ Is it a business logic error?
      └─ Use ErrorInfo.FromException(...) or custom ErrorInfo
```

---

## Performance Comparison

### Benchmark: Error Handling

```
| Method                  | Mean      | Allocated |
|------------------------ |----------:|----------:|
| ThrowAndCatchException  | 2,450 ns  |     336 B |
| ReturnErrorInfo         |     5 ns  |       0 B |
```

**490x faster, 0 allocations!**

---

## Best Practices

### ✅ Do

- Use `ErrorInfo` for expected errors
- Use error codes for pattern matching
- Mark errors as `IsRetryable` when appropriate
- Include context in `Details` field
- Return `CatgaResult.Failure(ErrorInfo)` from handlers

### ❌ Don't

- Don't throw exceptions for business logic errors
- Don't use magic strings for error codes (use `ErrorCodes` constants)
- Don't ignore `IsRetryable` flag
- Don't lose original exception context (use `ErrorInfo.FromException`)
- Don't mix exception throwing and error returning in the same handler

---

## Migration from Exceptions

### Before

```csharp
public async Task<OrderId> CreateOrderAsync(CreateOrder cmd)
{
    if (cmd.Amount <= 0)
        throw new ValidationException("Invalid amount");  // ❌
    
    return await _db.Orders.AddAsync(order);
}
```

### After

```csharp
public async Task<CatgaResult<OrderId>> CreateOrderAsync(CreateOrder cmd)
{
    if (cmd.Amount <= 0)
        return CatgaResult<OrderId>.Failure(
            ErrorInfo.Validation("Invalid amount", $"Amount: {cmd.Amount}"));  // ✅
    
    var orderId = await _db.Orders.AddAsync(order);
    return CatgaResult<OrderId>.Success(orderId);
}
```

---

## Summary

| Feature | Exceptions | ErrorInfo + CatgaResult |
|---------|-----------|------------------------|
| **Performance** | Slow (~2-5μs) | Fast (~5ns) |
| **Allocations** | Heap | Stack (struct) |
| **Retryability** | No metadata | `IsRetryable` flag |
| **Error Codes** | String parsing | Typed constants |
| **Control Flow** | Implicit | Explicit |
| **Best For** | Programming errors | Business errors |

**Use ErrorInfo for business logic, Exceptions for programming errors!**

