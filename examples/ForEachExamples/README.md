# ForEach Examples

This directory contains comprehensive examples demonstrating the ForEach functionality in Catga Flow DSL.

## Overview

The ForEach construct enables processing collections with:
- **Automatic recovery** from interruptions
- **Flexible error handling** strategies
- **Performance optimization** through batching
- **Progress tracking** across all storage backends

## Examples

### 1. OrderProcessingExample.cs

Real-world order processing scenarios:

- **BasicOrderProcessingFlow**: Simple inventory reservation and payment processing
- **SagaOrderProcessingFlow**: Advanced processing with compensation (Saga pattern)
- **HighVolumeOrderProcessingFlow**: Optimized for high-throughput scenarios
- **ConditionalOrderProcessingFlow**: Different logic based on item properties

### 2. ErrorHandlingExamples.cs

Comprehensive error handling patterns:

- **ContinueOnFailureFlow**: Process all items despite individual failures
- **StopOnFailureFlow**: Halt processing on first error
- **RetryWithBackoffFlow**: Automatic retry with exponential backoff
- **PriorityBasedProcessingFlow**: Priority-based processing with selective error handling
- **CircuitBreakerFlow**: Circuit breaker pattern for external service protection

## Key Features Demonstrated

### Error Handling Strategies

```csharp
// Continue processing despite failures
.ContinueOnFailure()
.OnItemFail((state, item, error) =>
{
    state.FailedItems.Add(item.Id);
    state.Errors[item.Id] = error;
})

// Stop on first failure
.StopOnFirstFailure()
.OnItemFail((state, item, error) =>
{
    state.Status = $"Critical failure: {error}";
})
```

### Performance Optimization

```csharp
// High-volume processing
.WithBatchSize(1000)  // Large batches for throughput
.OnItemSuccess((state, item, result) =>
{
    // Minimal processing for performance
    state.ProcessedCount++;
})
```

### Recovery and Compensation

```csharp
// Saga pattern with automatic compensation
f.Send(s => new ProcessTransaction(txn.Id))
 .Into(s => s.Results[txn.Id])
 .IfFail(s => new CompensateTransaction(txn.Id));
```

### Conditional Processing

```csharp
// Different logic based on item properties
f.If(s => item.Value > 1000)
    .Send(s => new HighValueProcessing(item))
.Else()
    .Send(s => new StandardProcessing(item))
.EndIf();
```

## Performance Characteristics

Based on performance tests:

- **Throughput**: 100+ items/second for typical workloads
- **Memory Usage**: Sub-linear growth with collection size (due to batching)
- **Recovery Overhead**: Minimal impact on processing speed
- **Batch Size Impact**: Optimal range 50-200 for most scenarios

## Best Practices

### 1. Choose Appropriate Batch Size
- **Small collections (< 100)**: Use batch size 10-20
- **Medium collections (100-1000)**: Use batch size 50-100
- **Large collections (> 1000)**: Use batch size 100-500

### 2. Error Handling Strategy
- **Critical operations**: Use `StopOnFirstFailure()`
- **Best-effort processing**: Use `ContinueOnFailure()`
- **High-availability**: Implement circuit breaker pattern

### 3. Performance Optimization
- Keep `OnItemSuccess` callbacks lightweight
- Use larger batch sizes for high-throughput scenarios
- Consider priority-based processing for mixed workloads

### 4. Recovery Design
- Design for idempotency
- Track progress in state objects
- Use compensation patterns for complex transactions

## Storage Backend Support

All examples work identically across storage backends:

```csharp
// InMemory
services.AddDslFlow().AddInMemoryDslFlowStore();

// Redis
services.AddDslFlow().AddRedisDslFlowStore(connectionString);

// NATS
services.AddDslFlow().AddNatsDslFlowStore(options);
```

## Testing

The examples include comprehensive test coverage:

- **Functional tests**: Verify correct processing logic
- **Performance tests**: Measure throughput and memory usage
- **Recovery tests**: Validate interruption and resume behavior
- **Error handling tests**: Confirm failure scenarios work correctly

## Integration

To use these patterns in your application:

1. Define your domain models and commands
2. Create a state class implementing `IFlowState`
3. Build your flow configuration using the ForEach patterns
4. Configure your DI container with the appropriate storage backend
5. Execute flows using `DslFlowExecutor<TState, TFlow>`

For complete integration examples, see the main Catga documentation.
