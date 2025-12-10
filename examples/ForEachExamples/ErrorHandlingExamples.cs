using Catga.Abstractions;
using Catga.Flow.Dsl;

namespace Catga.Examples.ForEachExamples;

/// <summary>
/// Examples demonstrating error handling and recovery patterns with ForEach.
/// </summary>
public class ErrorHandlingExamples
{
    // Domain Models
    public record DataItem(string Id, string Data, int Priority);
    public record ProcessingResult(string ItemId, bool Success, string Message);

    // Commands
    public record ProcessDataCommand(string ItemId, string Data) : IRequest<ProcessingResult>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record ValidateDataCommand(string Data) : IRequest<bool>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record RetryProcessCommand(string ItemId, string Data, int Attempt) : IRequest<ProcessingResult>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    // State
    public class DataProcessingState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<DataItem> Items { get; set; } = [];
        public Dictionary<string, ProcessingResult> Results { get; set; } = [];
        public Dictionary<string, string> Errors { get; set; } = [];
        public Dictionary<string, int> RetryAttempts { get; set; } = [];
        public List<string> FailedItems { get; set; } = [];
        public List<string> SkippedItems { get; set; } = [];
        public int TotalProcessed { get; set; }
        public int TotalFailed { get; set; }
        public string Status { get; set; } = "Processing";

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    /// <summary>
    /// Example 1: Continue on failure with detailed error tracking.
    /// </summary>
    public class ContinueOnFailureFlow : FlowConfig<DataProcessingState>
    {
        protected override void Configure(IFlowBuilder<DataProcessingState> flow)
        {
            flow.Name("continue-on-failure-processing");

            flow.ForEach<DataItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(10)
                .ContinueOnFailure() // Keep processing even if some items fail
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalProcessed++;
                    // Log successful processing
                    Console.WriteLine($"Successfully processed item {item.Id}");
                })
                .OnItemFail((state, item, error) =>
                {
                    state.TotalFailed++;
                    state.FailedItems.Add(item.Id);
                    state.Errors[item.Id] = error;

                    // Log detailed error information
                    Console.WriteLine($"Failed to process item {item.Id}: {error}");
                })
                .OnComplete(s =>
                {
                    s.Status = $"Completed: {s.TotalProcessed} successful, {s.TotalFailed} failed";
                    Console.WriteLine($"Processing complete: {s.Status}");
                })
            .EndForEach();

            // Post-processing: Handle failed items
            flow.If(s => s.FailedItems.Count > 0)
                .Send(s => Console.WriteLine($"Handling {s.FailedItems.Count} failed items"))
                // Could implement retry logic, alerting, etc.
            .EndIf();
        }
    }

    /// <summary>
    /// Example 2: Stop on first failure with immediate error handling.
    /// </summary>
    public class StopOnFailureFlow : FlowConfig<DataProcessingState>
    {
        protected override void Configure(IFlowBuilder<DataProcessingState> flow)
        {
            flow.Name("stop-on-failure-processing");

            flow.ForEach<DataItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Validate before processing
                    f.Send(s => new ValidateDataCommand(item.Data));

                    // Process only if validation passes
                    f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(5)
                .StopOnFirstFailure() // Stop immediately on any failure
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalProcessed++;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.TotalFailed++;
                    state.FailedItems.Add(item.Id);
                    state.Errors[item.Id] = error;
                    state.Status = $"Failed at item {item.Id}: {error}";
                })
                .OnComplete(s =>
                {
                    if (s.TotalFailed == 0)
                    {
                        s.Status = $"All {s.TotalProcessed} items processed successfully";
                    }
                })
            .EndForEach();
        }
    }

    /// <summary>
    /// Example 3: Automatic retry with exponential backoff.
    /// </summary>
    public class RetryWithBackoffFlow : FlowConfig<DataProcessingState>
    {
        protected override void Configure(IFlowBuilder<DataProcessingState> flow)
        {
            flow.Name("retry-with-backoff-processing");

            // Initial processing attempt
            flow.ForEach<DataItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(10)
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalProcessed++;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.FailedItems.Add(item.Id);
                    state.Errors[item.Id] = error;
                    state.RetryAttempts[item.Id] = 0; // Initialize retry counter
                })
            .EndForEach();

            // Retry failed items with exponential backoff
            flow.If(s => s.FailedItems.Count > 0)
                .ForEach<string>(s => s.FailedItems.ToList()) // Create copy to avoid modification during iteration
                    .Configure((itemId, f) =>
                    {
                        var item = f.GetState().Items.First(i => i.Id == itemId);
                        var attempt = f.GetState().RetryAttempts[itemId] + 1;

                        f.Send(s => new RetryProcessCommand(itemId, item.Data, attempt))
                         .Into(s => s.Results[itemId]);
                    })
                    .WithBatchSize(5) // Smaller batches for retries
                    .ContinueOnFailure()
                    .OnItemSuccess((state, itemId, result) =>
                    {
                        state.TotalProcessed++;
                        state.FailedItems.Remove(itemId);
                        state.Errors.Remove(itemId);
                        Console.WriteLine($"Retry successful for item {itemId}");
                    })
                    .OnItemFail((state, itemId, error) =>
                    {
                        state.RetryAttempts[itemId]++;
                        state.Errors[itemId] = error;

                        if (state.RetryAttempts[itemId] >= 3)
                        {
                            // Max retries reached, move to permanent failure
                            Console.WriteLine($"Max retries reached for item {itemId}");
                        }
                        else
                        {
                            Console.WriteLine($"Retry {state.RetryAttempts[itemId]} failed for item {itemId}");
                        }
                    })
                .EndForEach()
            .EndIf();

            // Final status update
            flow.Send(s =>
            {
                var permanentFailures = s.FailedItems.Where(id => s.RetryAttempts[id] >= 3).ToList();
                s.Status = $"Final: {s.TotalProcessed} processed, {permanentFailures.Count} permanent failures";
                return s.Status;
            });
        }
    }

    /// <summary>
    /// Example 4: Priority-based processing with selective error handling.
    /// </summary>
    public class PriorityBasedProcessingFlow : FlowConfig<DataProcessingState>
    {
        protected override void Configure(IFlowBuilder<DataProcessingState> flow)
        {
            flow.Name("priority-based-processing");

            // Process high-priority items first (priority 1)
            flow.ForEach<DataItem>(s => s.Items.Where(i => i.Priority == 1).ToList())
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(5)
                .StopOnFirstFailure() // High-priority items must succeed
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalProcessed++;
                    Console.WriteLine($"High-priority item {item.Id} processed successfully");
                })
                .OnItemFail((state, item, error) =>
                {
                    state.TotalFailed++;
                    state.FailedItems.Add(item.Id);
                    state.Errors[item.Id] = error;
                    state.Status = $"Critical failure in high-priority item {item.Id}";
                })
            .EndForEach();

            // Process medium-priority items (priority 2) - only if high-priority succeeded
            flow.If(s => s.TotalFailed == 0)
                .ForEach<DataItem>(s => s.Items.Where(i => i.Priority == 2).ToList())
                    .Configure((item, f) =>
                    {
                        f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                         .Into(s => s.Results[item.Id]);
                    })
                    .WithBatchSize(10)
                    .ContinueOnFailure() // Medium-priority can have some failures
                    .OnItemSuccess((state, item, result) =>
                    {
                        state.TotalProcessed++;
                    })
                    .OnItemFail((state, item, error) =>
                    {
                        state.FailedItems.Add(item.Id);
                        state.Errors[item.Id] = error;
                        Console.WriteLine($"Medium-priority item {item.Id} failed: {error}");
                    })
                .EndForEach()
            .EndIf();

            // Process low-priority items (priority 3) - best effort
            flow.If(s => s.TotalFailed == 0 || s.Items.Where(i => i.Priority <= 2).All(i => s.Results.ContainsKey(i.Id)))
                .ForEach<DataItem>(s => s.Items.Where(i => i.Priority == 3).ToList())
                    .Configure((item, f) =>
                    {
                        f.Send(s => new ProcessDataCommand(item.Id, item.Data))
                         .Into(s => s.Results[item.Id]);
                    })
                    .WithBatchSize(20)
                    .ContinueOnFailure() // Low-priority failures are acceptable
                    .OnItemSuccess((state, item, result) =>
                    {
                        state.TotalProcessed++;
                    })
                    .OnItemFail((state, item, error) =>
                    {
                        state.SkippedItems.Add(item.Id);
                        Console.WriteLine($"Low-priority item {item.Id} skipped due to error");
                    })
                    .OnComplete(s =>
                    {
                        s.Status = $"Processing complete: {s.TotalProcessed} processed, {s.SkippedItems.Count} skipped";
                    })
                .EndForEach()
            .EndIf();
        }
    }

    /// <summary>
    /// Example 5: Circuit breaker pattern for external service failures.
    /// </summary>
    public class CircuitBreakerFlow : FlowConfig<DataProcessingState>
    {
        protected override void Configure(IFlowBuilder<DataProcessingState> flow)
        {
            flow.Name("circuit-breaker-processing");

            // Add circuit breaker state to track consecutive failures
            flow.Send(s =>
            {
                s.Status = "Starting with circuit breaker protection";
                return "initialized";
            });

            flow.ForEach<DataItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Check circuit breaker state before processing
                    f.If(s => s.TotalFailed < 5) // Circuit is closed (working)
                        .Send(s => new ProcessDataCommand(item.Id, item.Data))
                         .Into(s => s.Results[item.Id])
                    .Else() // Circuit is open (too many failures)
                        .Send(s =>
                        {
                            s.SkippedItems.Add(item.Id);
                            return "skipped-circuit-open";
                        })
                    .EndIf();
                })
                .WithBatchSize(10)
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalProcessed++;
                    // Reset failure counter on success (half-open -> closed)
                    if (state.TotalFailed > 0)
                    {
                        Console.WriteLine("Circuit breaker: Service recovered, resetting failure count");
                        state.TotalFailed = Math.Max(0, state.TotalFailed - 1);
                    }
                })
                .OnItemFail((state, item, error) =>
                {
                    state.TotalFailed++;
                    state.FailedItems.Add(item.Id);
                    state.Errors[item.Id] = error;

                    if (state.TotalFailed >= 5)
                    {
                        Console.WriteLine("Circuit breaker: Too many failures, opening circuit");
                        state.Status = "Circuit breaker OPEN - Service unavailable";
                    }
                })
                .OnComplete(s =>
                {
                    if (s.TotalFailed >= 5)
                    {
                        s.Status = $"Processing halted: Circuit breaker open after {s.TotalFailed} failures";
                    }
                    else
                    {
                        s.Status = $"Processing complete: {s.TotalProcessed} processed, {s.TotalFailed} failed";
                    }
                })
            .EndForEach();
        }
    }
}
