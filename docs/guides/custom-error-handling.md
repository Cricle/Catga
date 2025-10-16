# Custom Error Handling in SafeRequestHandler

## Overview

`SafeRequestHandler` provides two virtual methods that allow you to customize how errors are handled:

- **`OnBusinessErrorAsync`** - Handle business logic errors (`CatgaException`)
- **`OnUnexpectedErrorAsync`** - Handle unexpected errors (all other exceptions)

Both methods have default implementations, but you can override them to add custom behavior like:
- Custom logging
- Error notification/alerting
- Error transformation
- Retry logic
- Metrics/telemetry
- Fallback responses

---

## Default Behavior

### Business Errors (CatgaException)

```csharp
protected virtual Task<CatgaResult<TResponse>> OnBusinessErrorAsync(
    TRequest request,
    CatgaException exception,
    CancellationToken cancellationToken)
{
    Logger.LogWarning(exception, "Business logic failed: {Message}", exception.Message);
    return Task.FromResult(CatgaResult<TResponse>.Failure(exception.Message, exception));
}
```

### Unexpected Errors (Exception)

```csharp
protected virtual Task<CatgaResult<TResponse>> OnUnexpectedErrorAsync(
    TRequest request,
    Exception exception,
    CancellationToken cancellationToken)
{
    Logger.LogError(exception, "Unexpected error in handler");
    return Task.FromResult(CatgaResult<TResponse>.Failure("Internal error", new CatgaException("Internal error", exception)));
}
```

---

## Common Use Cases

### 1. Custom Logging with Context

Add request-specific information to error logs:

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderResult>
{
    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) : base(logger) { }

    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // Business logic...
        throw new CatgaException("Insufficient inventory");
    }

    protected override Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(
        CreateOrderCommand request,
        CatgaException exception,
        CancellationToken cancellationToken)
    {
        // Custom logging with request context
        Logger.LogWarning(exception,
            "Order creation failed for Customer={CustomerId}, Items={ItemCount}: {Message}",
            request.CustomerId,
            request.Items.Count,
            exception.Message);

        return Task.FromResult(CatgaResult<OrderResult>.Failure(exception.Message, exception));
    }
}
```

### 2. Error Notification/Alerting

Send alerts for critical errors:

```csharp
public class PaymentHandler : SafeRequestHandler<ProcessPaymentCommand, PaymentResult>
{
    private readonly IAlertService _alertService;

    public PaymentHandler(ILogger<PaymentHandler> logger, IAlertService alertService)
        : base(logger)
    {
        _alertService = alertService;
    }

    protected override async Task<PaymentResult> HandleCoreAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken)
    {
        // Payment processing logic...
    }

    protected override async Task<CatgaResult<PaymentResult>> OnUnexpectedErrorAsync(
        ProcessPaymentCommand request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Alert on unexpected payment errors
        await _alertService.SendCriticalAlertAsync(
            $"Payment processing failed unexpectedly",
            new
            {
                OrderId = request.OrderId,
                Amount = request.Amount,
                Error = exception.Message,
                StackTrace = exception.StackTrace
            },
            cancellationToken);

        // Call base implementation
        return await base.OnUnexpectedErrorAsync(request, exception, cancellationToken);
    }
}
```

### 3. Error Transformation

Transform internal errors to user-friendly messages:

```csharp
public class GetUserProfileHandler : SafeRequestHandler<GetUserProfileQuery, UserProfile>
{
    public GetUserProfileHandler(ILogger<GetUserProfileHandler> logger) : base(logger) { }

    protected override async Task<UserProfile> HandleCoreAsync(
        GetUserProfileQuery request,
        CancellationToken cancellationToken)
    {
        // Query logic...
    }

    protected override Task<CatgaResult<UserProfile>> OnBusinessErrorAsync(
        GetUserProfileQuery request,
        CatgaException exception,
        CancellationToken cancellationToken)
    {
        // Transform error message based on error code
        var userMessage = exception.Message switch
        {
            var m when m.Contains("not found") => $"User profile for '{request.UserId}' does not exist",
            var m when m.Contains("access denied") => "You don't have permission to view this profile",
            var m when m.Contains("suspended") => "This user account has been suspended",
            _ => "Unable to retrieve user profile"
        };

        Logger.LogWarning(exception, "Profile query failed: {OriginalMessage}", exception.Message);

        return Task.FromResult(CatgaResult<UserProfile>.Failure(userMessage, exception));
    }
}
```

### 4. Retry Logic (Advanced)

Implement automatic retry for transient errors:

```csharp
public class ImportDataHandler : SafeRequestHandler<ImportDataCommand, ImportResult>
{
    private readonly IRetryPolicy _retryPolicy;

    public ImportDataHandler(ILogger<ImportDataHandler> logger, IRetryPolicy retryPolicy)
        : base(logger)
    {
        _retryPolicy = retryPolicy;
    }

    protected override async Task<ImportResult> HandleCoreAsync(
        ImportDataCommand request,
        CancellationToken cancellationToken)
    {
        // Data import logic...
    }

    protected override async Task<CatgaResult<ImportResult>> OnUnexpectedErrorAsync(
        ImportDataCommand request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Retry transient errors
        if (IsTransientError(exception))
        {
            Logger.LogInformation("Transient error detected, will retry: {Message}", exception.Message);

            try
            {
                var result = await _retryPolicy.ExecuteAsync(
                    async () => await HandleCoreAsync(request, cancellationToken),
                    cancellationToken);

                return CatgaResult<ImportResult>.Success(result);
            }
            catch (Exception retryException)
            {
                Logger.LogError(retryException, "Retry failed after transient error");
                return await base.OnUnexpectedErrorAsync(request, retryException, cancellationToken);
            }
        }

        return await base.OnUnexpectedErrorAsync(request, exception, cancellationToken);
    }

    private bool IsTransientError(Exception exception)
    {
        return exception is TimeoutException
            || exception is HttpRequestException
            || (exception.Message?.Contains("temporary", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
```

### 5. Metrics and Telemetry

Track error metrics:

```csharp
public class CheckoutHandler : SafeRequestHandler<CheckoutCommand, CheckoutResult>
{
    private readonly IMetricsCollector _metrics;

    public CheckoutHandler(ILogger<CheckoutHandler> logger, IMetricsCollector metrics)
        : base(logger)
    {
        _metrics = metrics;
    }

    protected override async Task<CheckoutResult> HandleCoreAsync(
        CheckoutCommand request,
        CancellationToken cancellationToken)
    {
        // Checkout logic...
    }

    protected override async Task<CatgaResult<CheckoutResult>> OnBusinessErrorAsync(
        CheckoutCommand request,
        CatgaException exception,
        CancellationToken cancellationToken)
    {
        // Track business error metrics
        _metrics.IncrementCounter("checkout.business_errors", new Dictionary<string, string>
        {
            ["error_type"] = GetErrorType(exception.Message),
            ["customer_id"] = request.CustomerId
        });

        return await base.OnBusinessErrorAsync(request, exception, cancellationToken);
    }

    protected override async Task<CatgaResult<CheckoutResult>> OnUnexpectedErrorAsync(
        CheckoutCommand request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Track unexpected error metrics
        _metrics.IncrementCounter("checkout.unexpected_errors", new Dictionary<string, string>
        {
            ["exception_type"] = exception.GetType().Name,
            ["customer_id"] = request.CustomerId
        });

        return await base.OnUnexpectedErrorAsync(request, exception, cancellationToken);
    }

    private string GetErrorType(string message)
    {
        if (message.Contains("inventory")) return "inventory";
        if (message.Contains("payment")) return "payment";
        if (message.Contains("validation")) return "validation";
        return "other";
    }
}
```

### 6. Fallback Response

Provide fallback responses for certain errors:

```csharp
public class GetRecommendationsHandler : SafeRequestHandler<GetRecommendationsQuery, RecommendationResult>
{
    public GetRecommendationsHandler(ILogger<GetRecommendationsHandler> logger)
        : base(logger) { }

    protected override async Task<RecommendationResult> HandleCoreAsync(
        GetRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        // Recommendation logic...
    }

    protected override Task<CatgaResult<RecommendationResult>> OnUnexpectedErrorAsync(
        GetRecommendationsQuery request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(exception, "Recommendation service failed, returning fallback results");

        // Return empty recommendations instead of failing
        var fallbackResult = new RecommendationResult
        {
            Items = new List<RecommendationItem>(),
            Message = "Recommendations are temporarily unavailable"
        };

        return Task.FromResult(CatgaResult<RecommendationResult>.Success(fallbackResult));
    }
}
```

---

## Best Practices

### ✅ Do

1. **Call base implementation** when adding custom behavior:
   ```csharp
   protected override async Task<CatgaResult<TResponse>> OnBusinessErrorAsync(...)
   {
       // Custom logic
       await _notificationService.NotifyAsync(...);

       // Then call base
       return await base.OnBusinessErrorAsync(request, exception, cancellationToken);
   }
   ```

2. **Use async/await properly** for I/O operations:
   ```csharp
   protected override async Task<CatgaResult<TResponse>> OnUnexpectedErrorAsync(...)
   {
       await _alertService.SendAlertAsync(...);
       return await base.OnUnexpectedErrorAsync(request, exception, cancellationToken);
   }
   ```

3. **Keep error handling logic simple** - complex logic should be in services:
   ```csharp
   // ✅ Good
   protected override async Task<CatgaResult<TResponse>> OnBusinessErrorAsync(...)
   {
       await _errorHandler.HandleBusinessErrorAsync(request, exception);
       return await base.OnBusinessErrorAsync(request, exception, cancellationToken);
   }

   // ❌ Bad - too much logic in override
   protected override async Task<CatgaResult<TResponse>> OnBusinessErrorAsync(...)
   {
       // 50 lines of complex error handling logic...
   }
   ```

4. **Preserve original exception** for debugging:
   ```csharp
   return CatgaResult<TResponse>.Failure(userFriendlyMessage, originalException);
   ```

### ❌ Don't

1. **Don't swallow exceptions** without logging:
   ```csharp
   // ❌ Bad - exception is lost
   protected override Task<CatgaResult<TResponse>> OnUnexpectedErrorAsync(...)
   {
       return Task.FromResult(CatgaResult<TResponse>.Success(default!));
   }
   ```

2. **Don't throw exceptions** from error handlers:
   ```csharp
   // ❌ Bad - will cause unhandled exception
   protected override Task<CatgaResult<TResponse>> OnBusinessErrorAsync(...)
   {
       throw new Exception("Error in error handler");
   }
   ```

3. **Don't perform heavy operations** without timeouts:
   ```csharp
   // ❌ Bad - could hang indefinitely
   protected override async Task<CatgaResult<TResponse>> OnUnexpectedErrorAsync(...)
   {
       await _service.HeavyOperationWithoutTimeoutAsync();
       return await base.OnUnexpectedErrorAsync(request, exception, cancellationToken);
   }
   ```

---

## Testing Custom Error Handlers

Example unit test:

```csharp
[Fact]
public async Task OnBusinessErrorAsync_ShouldSendAlert()
{
    // Arrange
    var mockAlertService = new Mock<IAlertService>();
    var handler = new PaymentHandler(
        NullLogger<PaymentHandler>.Instance,
        mockAlertService.Object);

    var request = new ProcessPaymentCommand("order-1", 100m);
    var exception = new CatgaException("Payment gateway timeout");

    // Act - Use reflection to call protected method in test
    var method = typeof(PaymentHandler).GetMethod(
        "OnBusinessErrorAsync",
        BindingFlags.NonPublic | BindingFlags.Instance);

    await (Task<CatgaResult<PaymentResult>>)method!.Invoke(
        handler,
        new object[] { request, exception, CancellationToken.None })!;

    // Assert
    mockAlertService.Verify(
        x => x.SendAlertAsync(
            It.Is<string>(s => s.Contains("Payment")),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

---

## Summary

Custom error handling in `SafeRequestHandler` allows you to:

- ✅ Add contextual logging
- ✅ Send alerts/notifications
- ✅ Transform error messages
- ✅ Implement retry logic
- ✅ Collect metrics
- ✅ Provide fallback responses

**Default behavior is production-ready, but override for custom needs!**

