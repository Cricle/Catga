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

Catga uses **10 simple, readable error codes**:

```csharp
public static class ErrorCodes
{
    // Core error codes - simple and focused
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string HandlerFailed = "HANDLER_FAILED";
    public const string PipelineFailed = "PIPELINE_FAILED";
    public const string PersistenceFailed = "PERSISTENCE_FAILED";
    public const string LockFailed = "LOCK_FAILED";
    public const string TransportFailed = "TRANSPORT_FAILED";
    public const string SerializationFailed = "SERIALIZATION_FAILED";
    public const string Timeout = "TIMEOUT";
    public const string Cancelled = "CANCELLED";
    public const string InternalError = "INTERNAL_ERROR";
}
```

**Philosophy**: Simple > Perfect categorization. 10 codes cover all scenarios.

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
        case ErrorCodes.ValidationFailed:
            _logger.LogWarning("Validation failed: {Error}", result.Error);
            return BadRequest(result.Error);
        
        case ErrorCodes.PersistenceFailed:
        case ErrorCodes.LockFailed:
            _logger.LogWarning("Retryable error: {Error}", result.Error);
            // Retry or return 503
            return StatusCode(503, "Service temporarily unavailable");
        
        case ErrorCodes.HandlerFailed:
        case ErrorCodes.PipelineFailed:
            _logger.LogError("Execution failed: {Error}", result.Error);
            return StatusCode(500, "Internal server error");
        
        case ErrorCodes.Timeout:
        case ErrorCodes.Cancelled:
            return StatusCode(408, "Request timeout");
        
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
        ErrorCodes.LockFailed => true,
        ErrorCodes.PersistenceFailed => true,
        ErrorCodes.TransportFailed => true,
        ErrorCodes.Timeout => true,
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



