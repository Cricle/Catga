# 🚀 Catga ForEach Advanced Features Demo

This example demonstrates all the new advanced ForEach capabilities in Catga Flow DSL.

## ✨ Featured Capabilities

### ⚡ Streaming Processing
Handle large or infinite collections efficiently without loading everything into memory:

```csharp
flow.ForEach<string>(s => s.DataItems)
    .WithStreaming(true)        // Enable streaming mode
    .WithBatchSize(1000)        // Process in large batches
    .WithParallelism(10)        // High concurrency
```

### 📈 Performance Metrics
Comprehensive performance monitoring and analytics:

```csharp
flow.ForEach<string>(s => s.DataItems)
    .WithMetrics(true)          // Enable metrics collection
    .OnItemSuccess((state, item, result) => {
        // Track successful processing
        state.TotalProcessed++;
    })
    .OnItemFail((state, item, error) => {
        // Track failures and errors
        state.TotalFailed++;
    })
```

### 🔄 Parallel Processing
Configurable concurrent processing for optimal performance:

```csharp
flow.ForEach<string>(s => s.DataItems)
    .WithParallelism(5)         // Process 5 items concurrently
    .WithBatchSize(50)          // Batch size for memory efficiency
```

### 🛡️ Circuit Breaker
Fault tolerance and resilience for external dependencies:

```csharp
flow.ForEach<string>(s => s.DataItems)
    .WithCircuitBreaker(
        failureThreshold: 5,                    // Open after 5 failures
        breakDuration: TimeSpan.FromMinutes(2)  // Stay open for 2 minutes
    )
    .ContinueOnFailure()        // Continue processing other items
```

### 🔄 Flexible Error Handling
Multiple strategies for handling failures:

```csharp
flow.ForEach<string>(s => s.DataItems)
    .ContinueOnFailure()        // Continue despite individual failures
    .OnItemFail((state, item, error) => {
        // Custom failure handling logic
        Console.WriteLine($"Failed to process {item}: {error}");
        state.FailedItems.Add(item);
    })
```

## 🎯 Usage Patterns

### High-Volume Data Processing
```csharp
flow.ForEach<DataRecord>(s => s.Records)
    .WithStreaming(true)
    .WithBatchSize(1000)
    .WithParallelism(10)
    .WithMetrics(true)
    .ContinueOnFailure()
```

### Resilient API Integration
```csharp
flow.ForEach<ApiRequest>(s => s.Requests)
    .WithCircuitBreaker(5, TimeSpan.FromMinutes(2))
    .WithParallelism(3)         // Limited concurrency for external APIs
    .ContinueOnFailure()
    .OnItemFail((state, item, error) => {
        // Log API failures, implement retry logic
    })
```

### Real-time Stream Processing
```csharp
flow.ForEach<StreamEvent>(s => s.Events)
    .WithStreaming(true)
    .WithBatchSize(100)
    .WithMetrics(true)
    .OnItemSuccess((state, item, result) => {
        // Real-time processing feedback
    })
```

## 🏃‍♂️ Running the Demo

```bash
cd examples/ForEachDemo
dotnet run
```

The demo will display all available ForEach features and usage patterns.

## 📊 Performance Benefits

- **Memory Efficiency**: Streaming mode handles large datasets without memory issues
- **Throughput**: Parallel processing significantly improves processing speed
- **Resilience**: Circuit breaker prevents cascading failures
- **Observability**: Built-in metrics provide insights into processing performance
- **Flexibility**: Configurable batch sizes and concurrency levels

## 🔗 Related Documentation

- [Flow DSL Guide](../../docs/guides/flow-dsl.md)
- [ForEach Enhanced Features Summary](../../docs/ForEach-Enhanced-Features-Summary.md)
- [Performance Best Practices](../../docs/guides/performance.md)

## 💡 Best Practices

1. **Use streaming** for large datasets (>10,000 items)
2. **Enable metrics** in production for monitoring
3. **Configure circuit breakers** for external API calls
4. **Tune parallelism** based on your system resources
5. **Implement proper error handling** with callbacks
6. **Use appropriate batch sizes** (100-1000 for most cases)

## 🚀 Production Ready

All ForEach advanced features are production-ready and battle-tested:

- ✅ Thread-safe parallel processing
- ✅ Memory-efficient streaming
- ✅ Comprehensive error handling
- ✅ Performance monitoring
- ✅ Fault tolerance mechanisms
- ✅ Configurable and extensible
