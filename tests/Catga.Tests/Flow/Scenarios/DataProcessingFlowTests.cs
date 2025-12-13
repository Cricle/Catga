using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Data processing workflow scenarios.
/// Tests ETL-like patterns, batch processing, and data transformation pipelines.
/// </summary>
public class DataProcessingFlowTests
{
    #region Test State

    public class EtlState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string SourceName { get; set; } = "";

        // Extract phase
        public List<RawRecord> RawRecords { get; set; } = new();
        public int ExtractedCount { get; set; }

        // Transform phase
        public List<TransformedRecord> TransformedRecords { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
        public int TransformedCount { get; set; }
        public int SkippedCount { get; set; }

        // Load phase
        public int LoadedCount { get; set; }
        public bool LoadCompleted { get; set; }

        // Metrics
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<string> ProcessingLog { get; set; } = new();
    }

    public record RawRecord(string Id, string Data, string Type, bool IsValid);
    public record TransformedRecord(string Id, string ProcessedData, string Category, DateTime ProcessedAt);

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EtlPipeline_CompleteFlow_ProcessesAllRecords()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EtlState>("etl-pipeline")
            // Extract
            .Step("extract", async (state, ct) =>
            {
                state.StartTime = DateTime.UtcNow;
                state.ProcessingLog.Add("Extract started");
                state.ExtractedCount = state.RawRecords.Count;
                state.ProcessingLog.Add($"Extracted {state.ExtractedCount} records");
                return true;
            })
            // Transform with validation
            .ForEach(
                s => s.RawRecords,
                (record, f) => f
                    .If(s => record.IsValid)
                        .Then(inner => inner.Step($"transform-{record.Id}", async (state, ct) =>
                        {
                            var transformed = new TransformedRecord(
                                record.Id,
                                record.Data.ToUpperInvariant(),
                                record.Type,
                                DateTime.UtcNow);
                            state.TransformedRecords.Add(transformed);
                            state.TransformedCount++;
                            return true;
                        }))
                        .Else(inner => inner.Step($"skip-{record.Id}", async (state, ct) =>
                        {
                            state.ValidationErrors.Add($"Record {record.Id} is invalid");
                            state.SkippedCount++;
                            return true;
                        }))
                    .EndIf())
            // Load
            .Step("load", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Loading {state.TransformedCount} records");
                state.LoadedCount = state.TransformedCount;
                state.LoadCompleted = true;
                return true;
            })
            // Finalize
            .Step("finalize", async (state, ct) =>
            {
                state.EndTime = DateTime.UtcNow;
                state.ProcessingLog.Add($"ETL completed: {state.LoadedCount} loaded, {state.SkippedCount} skipped");
                return true;
            })
            .Build();

        var state = new EtlState
        {
            FlowId = "etl-test",
            SourceName = "TestSource",
            RawRecords = new List<RawRecord>
            {
                new("R001", "data one", "TypeA", true),
                new("R002", "data two", "TypeB", true),
                new("R003", "invalid", "TypeA", false),
                new("R004", "data four", "TypeC", true),
                new("R005", "also invalid", "TypeB", false)
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExtractedCount.Should().Be(5);
        result.State.TransformedCount.Should().Be(3);
        result.State.SkippedCount.Should().Be(2);
        result.State.LoadedCount.Should().Be(3);
        result.State.LoadCompleted.Should().BeTrue();
        result.State.ValidationErrors.Should().HaveCount(2);
    }

    [Fact]
    public async Task BatchProcessing_LargeBatch_ProcessesInChunks()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BatchState>("batch-processing")
            .Step("init", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Processing {state.Items.Count} items in batches of {state.BatchSize}");
                return true;
            })
            .While(s => s.CurrentBatch * s.BatchSize < s.Items.Count)
                .Do(f => f.Step("process-batch", async (state, ct) =>
                {
                    var skip = state.CurrentBatch * state.BatchSize;
                    var batch = state.Items.Skip(skip).Take(state.BatchSize).ToList();

                    state.ProcessingLog.Add($"Processing batch {state.CurrentBatch + 1}: {batch.Count} items");

                    foreach (var item in batch)
                    {
                        state.ProcessedItems.Add($"{item}-processed");
                    }

                    state.CurrentBatch++;
                    return true;
                }))
            .EndWhile()
            .Step("complete", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Completed: {state.ProcessedItems.Count} items processed in {state.CurrentBatch} batches");
                return true;
            })
            .Build();

        var state = new BatchState
        {
            FlowId = "batch-test",
            BatchSize = 3,
            Items = Enumerable.Range(1, 10).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(10);
        result.State.CurrentBatch.Should().Be(4); // 10 items / 3 per batch = 4 batches
    }

    [Fact]
    public async Task DataAggregation_GroupAndSummarize_ProducesCorrectResults()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<AggregationState>("data-aggregation")
            .Step("load-data", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Loaded {state.Records.Count} records");
                return true;
            })
            .Step("group-by-category", async (state, ct) =>
            {
                var groups = state.Records.GroupBy(r => r.Category);
                foreach (var group in groups)
                {
                    state.CategoryCounts[group.Key] = group.Count();
                    state.CategoryTotals[group.Key] = group.Sum(r => r.Amount);
                }
                state.ProcessingLog.Add($"Grouped into {state.CategoryCounts.Count} categories");
                return true;
            })
            .Step("calculate-summary", async (state, ct) =>
            {
                state.TotalRecords = state.Records.Count;
                state.TotalAmount = state.Records.Sum(r => r.Amount);
                state.AverageAmount = state.TotalAmount / state.TotalRecords;
                state.MaxAmount = state.Records.Max(r => r.Amount);
                state.MinAmount = state.Records.Min(r => r.Amount);
                state.ProcessingLog.Add($"Summary: Total={state.TotalAmount:F2}, Avg={state.AverageAmount:F2}");
                return true;
            })
            .Build();

        var state = new AggregationState
        {
            FlowId = "agg-test",
            Records = new List<DataRecord>
            {
                new("Electronics", 100m),
                new("Electronics", 200m),
                new("Electronics", 150m),
                new("Clothing", 50m),
                new("Clothing", 75m),
                new("Books", 25m),
                new("Books", 30m),
                new("Books", 20m)
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CategoryCounts["Electronics"].Should().Be(3);
        result.State.CategoryCounts["Clothing"].Should().Be(2);
        result.State.CategoryCounts["Books"].Should().Be(3);
        result.State.CategoryTotals["Electronics"].Should().Be(450m);
        result.State.TotalAmount.Should().Be(650m);
        result.State.TotalRecords.Should().Be(8);
    }

    [Fact]
    public async Task DataValidation_WithRules_AppliesAllRules()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ValidationState>("data-validation")
            .ForEach(
                s => s.Records,
                (record, f) => f
                    .Step($"validate-{record.Id}", async (state, ct) =>
                    {
                        var errors = new List<string>();

                        // Rule 1: Name required
                        if (string.IsNullOrWhiteSpace(record.Name))
                            errors.Add($"{record.Id}: Name is required");

                        // Rule 2: Amount must be positive
                        if (record.Amount <= 0)
                            errors.Add($"{record.Id}: Amount must be positive");

                        // Rule 3: Category must be valid
                        var validCategories = new[] { "A", "B", "C" };
                        if (!validCategories.Contains(record.Category))
                            errors.Add($"{record.Id}: Invalid category '{record.Category}'");

                        if (errors.Any())
                        {
                            state.InvalidRecords.Add(record.Id);
                            state.ValidationErrors.AddRange(errors);
                        }
                        else
                        {
                            state.ValidRecords.Add(record.Id);
                        }

                        return true;
                    }))
            .Step("summarize", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Valid: {state.ValidRecords.Count}, Invalid: {state.InvalidRecords.Count}");
                return true;
            })
            .Build();

        var state = new ValidationState
        {
            FlowId = "validation-test",
            Records = new List<ValidationRecord>
            {
                new("R1", "Item 1", 100m, "A"),   // Valid
                new("R2", "", 50m, "B"),           // Invalid: no name
                new("R3", "Item 3", -10m, "A"),    // Invalid: negative amount
                new("R4", "Item 4", 200m, "D"),    // Invalid: bad category
                new("R5", "Item 5", 75m, "C")      // Valid
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ValidRecords.Should().HaveCount(2);
        result.State.InvalidRecords.Should().HaveCount(3);
        result.State.ValidationErrors.Should().HaveCount(3);
    }

    [Fact]
    public async Task DataEnrichment_JoinsExternalData_EnrichesRecords()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EnrichmentState>("data-enrichment")
            .Step("load-reference-data", async (state, ct) =>
            {
                // Simulate loading reference data
                state.ReferenceData["C001"] = "Premium Customer";
                state.ReferenceData["C002"] = "Regular Customer";
                state.ReferenceData["C003"] = "VIP Customer";
                state.ProcessingLog.Add("Reference data loaded");
                return true;
            })
            .ForEach(
                s => s.Orders,
                (order, f) => f.Step($"enrich-{order.OrderId}", async (state, ct) =>
                {
                    var customerType = state.ReferenceData.GetValueOrDefault(order.CustomerId, "Unknown");
                    var enriched = new EnrichedOrder(order.OrderId, order.CustomerId, customerType, order.Amount);
                    state.EnrichedOrders.Add(enriched);
                    return true;
                }))
            .Step("complete", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Enriched {state.EnrichedOrders.Count} orders");
                return true;
            })
            .Build();

        var state = new EnrichmentState
        {
            FlowId = "enrich-test",
            Orders = new List<Order>
            {
                new("O001", "C001", 100m),
                new("O002", "C002", 200m),
                new("O003", "C003", 300m),
                new("O004", "C999", 50m) // Unknown customer
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.EnrichedOrders.Should().HaveCount(4);
        result.State.EnrichedOrders.First(o => o.OrderId == "O001").CustomerType.Should().Be("Premium Customer");
        result.State.EnrichedOrders.First(o => o.OrderId == "O004").CustomerType.Should().Be("Unknown");
    }

    #region Supporting Types

    public class BatchState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public int BatchSize { get; set; } = 10;
        public int CurrentBatch { get; set; }
        public List<string> ProcessedItems { get; set; } = new();
        public List<string> ProcessingLog { get; set; } = new();
    }

    public class AggregationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<DataRecord> Records { get; set; } = new();
        public Dictionary<string, int> CategoryCounts { get; set; } = new();
        public Dictionary<string, decimal> CategoryTotals { get; set; } = new();
        public int TotalRecords { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AverageAmount { get; set; }
        public decimal MaxAmount { get; set; }
        public decimal MinAmount { get; set; }
        public List<string> ProcessingLog { get; set; } = new();
    }

    public record DataRecord(string Category, decimal Amount);

    public class ValidationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<ValidationRecord> Records { get; set; } = new();
        public List<string> ValidRecords { get; set; } = new();
        public List<string> InvalidRecords { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
        public List<string> ProcessingLog { get; set; } = new();
    }

    public record ValidationRecord(string Id, string Name, decimal Amount, string Category);

    public class EnrichmentState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<Order> Orders { get; set; } = new();
        public Dictionary<string, string> ReferenceData { get; set; } = new();
        public List<EnrichedOrder> EnrichedOrders { get; set; } = new();
        public List<string> ProcessingLog { get; set; } = new();
    }

    public record Order(string OrderId, string CustomerId, decimal Amount);
    public record EnrichedOrder(string OrderId, string CustomerId, string CustomerType, decimal Amount);

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
